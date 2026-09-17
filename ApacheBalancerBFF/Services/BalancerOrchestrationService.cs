using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Domain;
using Shared.Dtos;
using Microsoft.Extensions.Options;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Default <see cref="IBalancerOrchestrationService"/>: fans out over the configured servers,
/// shields individual failures (partial success) and drives the nonce → POST → confirm workflow.
/// </summary>
public sealed class BalancerOrchestrationService : IBalancerOrchestrationService
{
    private readonly ApacheManagementOptions _options;
    private readonly IBalancerManagerClient _client;
    private readonly IBalancerHtmlParser _parser;
    private readonly ITrafficMetricsClient _trafficMetrics;
    private readonly ILogger<BalancerOrchestrationService> _logger;

    public BalancerOrchestrationService(
        IOptions<ApacheManagementOptions> options,
        IBalancerManagerClient client,
        IBalancerHtmlParser parser,
        ITrafficMetricsClient trafficMetrics,
        ILogger<BalancerOrchestrationService> logger)
    {
        _options = options.Value;
        _client = client;
        _parser = parser;
        _trafficMetrics = trafficMetrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MultiServerResponseDto<ServerStatusDto>> GetClusterStatusAsync(CancellationToken cancellationToken)
    {
        MultiServerResponseDto<ServerStatusDto> response = new();

        if (_options.Servers.Count == 0)
        {
            _logger.LogWarning("No Apache servers configured under '{Section}:Servers'.", ApacheManagementOptions.SectionName);
            return response;
        }

        List<Task<(ServerStatusDto? Status, ServerErrorDto? Error)>> tasks = _options.Servers
            .Select((ApacheServerOptions server) => FetchServerStatusAsync(server, cancellationToken))
            .ToList();

        (ServerStatusDto? Status, ServerErrorDto? Error)[] results = await Task.WhenAll(tasks);

        foreach ((ServerStatusDto? status, ServerErrorDto? error) in results)
        {
            if (status is not null)
            {
                response.SuccessData.Add(status);
            }
            else if (error is not null)
            {
                response.Errors.Add(error);
            }
        }

        return response;
    }

    /// <inheritdoc />
    public async Task<WorkerActionOutcome> ExecuteWorkerActionAsync(WorkerActionRequestDto request, CancellationToken cancellationToken)
    {
        ApacheServerOptions? server = _options.Servers
            .FirstOrDefault((ApacheServerOptions candidate) => string.Equals(candidate.Id, request.ServerId, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            string knownIds = string.Join(", ", _options.Servers.Select((ApacheServerOptions candidate) => candidate.Id));
            return WorkerActionOutcome.Failure(
                StatusCodes.Status400BadRequest,
                $"Unknown ServerId '{request.ServerId}'. Configured server ids: {knownIds}.");
        }

        if (!Enum.TryParse(request.ActionType, ignoreCase: true, out WorkerActionType actionType))
        {
            string validActions = string.Join(", ", Enum.GetNames<WorkerActionType>());
            return WorkerActionOutcome.Failure(
                StatusCodes.Status400BadRequest,
                $"Unknown ActionType '{request.ActionType}'. Valid values: {validActions}.");
        }

        // Step 1: fetch the status page to obtain a fresh per-balancer nonce.
        string statusHtml;
        try
        {
            statusHtml = await _client.GetStatusPageAsync(server, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return WorkerActionOutcome.Failure(
                StatusCodes.Status502BadGateway,
                $"Could not reach '{server.BaseUrl}/balancer-manager' on server '{server.Id}' to obtain a fresh nonce: {DescribeException(exception)}");
        }

        List<BalancerStatusDto> balancers = _parser.ParseBalancers(statusHtml);

        BalancerStatusDto? balancer = balancers
            .FirstOrDefault((BalancerStatusDto candidate) => string.Equals(candidate.Name, request.BalancerName, StringComparison.OrdinalIgnoreCase));

        if (balancer is null)
        {
            string knownBalancers = string.Join(", ", balancers.Select((BalancerStatusDto candidate) => candidate.Name));
            return WorkerActionOutcome.Failure(
                StatusCodes.Status400BadRequest,
                $"Balancer '{request.BalancerName}' was not found on server '{server.Id}'. Available balancers: {knownBalancers}.");
        }

        WorkerStatusDto? worker = balancer.Workers
            .FirstOrDefault((WorkerStatusDto candidate) => string.Equals(candidate.Url, request.WorkerUrl, StringComparison.OrdinalIgnoreCase));

        if (worker is null)
        {
            string knownWorkers = string.Join(", ", balancer.Workers.Select((WorkerStatusDto candidate) => candidate.Url));
            return WorkerActionOutcome.Failure(
                StatusCodes.Status400BadRequest,
                $"Worker '{request.WorkerUrl}' was not found in balancer '{balancer.Name}' on server '{server.Id}'. Available workers: {knownWorkers}.");
        }

        if (string.IsNullOrEmpty(balancer.Nonce))
        {
            return WorkerActionOutcome.Failure(
                StatusCodes.Status502BadGateway,
                $"A nonce could not be scraped for balancer '{balancer.Name}' on server '{server.Id}'; Apache may have changed its HTML layout.");
        }

        // Step 2: build the minimal form payload Apache expects (b, w, nonce + status flags).
        Dictionary<string, string> formFields = new()
        {
            ["b"] = balancer.Name,
            ["w"] = worker.Url,
            ["nonce"] = balancer.Nonce
        };

        foreach (KeyValuePair<string, string> field in WorkerActionMapper.ToFormFields(actionType))
        {
            formFields[field.Key] = field.Value;
        }

        // Step 3: submit the modification.
        string responseHtml;
        try
        {
            responseHtml = await _client.PostWorkerFormAsync(server, formFields, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return WorkerActionOutcome.Failure(
                StatusCodes.Status502BadGateway,
                $"The modification POST to '{server.BaseUrl}/balancer-manager' on server '{server.Id}' failed: {DescribeException(exception)}");
        }

        // Step 4: Apache answers the POST with the refreshed page — parse it to report the new state.
        WorkerStatusDto? updatedWorker = null;
        List<BalancerStatusDto> refreshedBalancers = _parser.ParseBalancers(responseHtml);
        BalancerStatusDto? refreshedBalancer = refreshedBalancers
            .FirstOrDefault((BalancerStatusDto candidate) => string.Equals(candidate.Name, balancer.Name, StringComparison.OrdinalIgnoreCase));

        if (refreshedBalancer is not null)
        {
            updatedWorker = refreshedBalancer.Workers
                .FirstOrDefault((WorkerStatusDto candidate) => string.Equals(candidate.Url, worker.Url, StringComparison.OrdinalIgnoreCase));
        }

        if (updatedWorker is null)
        {
            _logger.LogWarning(
                "Could not re-locate worker {Worker} in balancer {Balancer} on server {ServerId} in Apache's POST response; returning pre-action state.",
                worker.Url,
                balancer.Name,
                server.Id);
        }
        else
        {
            // Step 5: confirm the scraped state matches what the action should have produced.
            // Apache answers HTTP 200 even when it silently ignores a modification (e.g. its
            // CSRF protection rejected the request), so a 200 alone proves nothing.
            List<string> discrepancies = new();
            foreach (KeyValuePair<string, bool> expectation in WorkerActionMapper.ToExpectedFlagStates(actionType))
            {
                bool flagIsPresent = updatedWorker.StatusFlags
                    .Contains(expectation.Key, StringComparer.OrdinalIgnoreCase);

                if (flagIsPresent != expectation.Value)
                {
                    discrepancies.Add(expectation.Value
                        ? $"flag '{expectation.Key}' is still missing"
                        : $"flag '{expectation.Key}' is still present");
                }
            }

            if (discrepancies.Count > 0)
            {
                return WorkerActionOutcome.Failure(
                    StatusCodes.Status502BadGateway,
                    $"Apache on server '{server.Id}' accepted the request (HTTP 200) but did not apply '{actionType}' " +
                    $"to worker '{worker.Url}' in balancer '{balancer.Name}': {string.Join(", ", discrepancies)} " +
                    $"(post-action status: '{updatedWorker.RawStatus}').");
            }
        }

        _logger.LogInformation(
            "Applied {Action} to worker {Worker} (balancer {Balancer}) on server {ServerId}. New status: {Status}",
            actionType,
            worker.Url,
            balancer.Name,
            server.Id,
            updatedWorker?.RawStatus ?? "unknown");

        WorkerActionResultDto result = new()
        {
            ServerId = server.Id,
            BalancerName = balancer.Name,
            WorkerUrl = worker.Url,
            ActionApplied = actionType.ToString(),
            NewStatus = updatedWorker ?? worker
        };

        return WorkerActionOutcome.Success(result);
    }

    private async Task<(ServerStatusDto? Status, ServerErrorDto? Error)> FetchServerStatusAsync(ApacheServerOptions server, CancellationToken cancellationToken)
    {
        try
        {
            Task<string> statusTask = _client.GetStatusPageAsync(server, cancellationToken);

            // Optional and best-effort: this one never throws, so awaiting it first cannot mask a
            // failure of the status page itself.
            Task<TrafficSnapshot?> trafficTask = _trafficMetrics.GetSnapshotAsync(server, cancellationToken);
            TrafficSnapshot? traffic = await trafficTask;

            string html = await statusTask;
            List<BalancerStatusDto> balancers = _parser.ParseBalancers(html);

            if (traffic is not null)
            {
                ApplyTrafficCounters(balancers, traffic);
            }

            ServerStatusDto status = new()
            {
                ServerId = server.Id,
                ServerName = server.Name,
                BaseUrl = server.BaseUrl,
                Balancers = balancers
            };

            return (status, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            _logger.LogError(exception, "Failed to fetch balancer status from server {ServerId} ({BaseUrl}).", server.Id, server.BaseUrl);

            ServerErrorDto error = new()
            {
                ServerId = server.Id,
                ErrorMessage = DescribeException(exception),
                Timestamp = DateTimeOffset.UtcNow
            };

            return (null, error);
        }
    }

    /// <summary>
    /// Stamps the exact byte totals onto every worker of a server whose snapshot was readable.
    /// </summary>
    /// <remarks>
    /// A worker the snapshot does not list gets zero rather than null, and that is exact rather than
    /// a guess: the aggregator counts from its own start, so a worker with no log line has carried no
    /// bytes since then. The dashboard only ever plots deltas taken from samples it collected itself,
    /// all of which are later than the snapshot's start, so zero is the right baseline even when the
    /// aggregator was started long after Apache. Leaving these null instead would strand the whole
    /// pool on the rounded cells whenever any one of its workers happened to be idle.
    /// </remarks>
    private static void ApplyTrafficCounters(List<BalancerStatusDto> balancers, TrafficSnapshot traffic)
    {
        foreach (BalancerStatusDto balancer in balancers)
        {
            foreach (WorkerStatusDto worker in balancer.Workers)
            {
                bool isKnown = traffic.TryGetCounters(balancer.Name, worker.Url, out WorkerTrafficCounters counters);

                worker.ToBytes = isKnown ? counters.ToBytes : 0L;
                worker.FromBytes = isKnown ? counters.FromBytes : 0L;
            }
        }
    }

    private static string DescribeException(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return "Connection timed out.";
        }

        if (exception is HttpRequestException httpException && httpException.StatusCode is not null)
        {
            return $"Upstream returned HTTP {(int)httpException.StatusCode} ({httpException.StatusCode}).";
        }

        return exception.InnerException?.Message ?? exception.Message;
    }
}
