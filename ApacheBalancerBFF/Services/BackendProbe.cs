using System.Diagnostics;
using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Domain;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Default <see cref="IBackendProbe"/>: a plain GET, with the deadline enforced by a cancellation
/// token rather than by <see cref="HttpClient.Timeout"/>, so the elapsed time can still be
/// reported when the budget runs out.
/// </summary>
public sealed class BackendProbe : IBackendProbe
{
    /// <summary>Name of the named <see cref="HttpClient"/> this probe uses.</summary>
    public const string HttpClientName = "watchdog-probe";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackendProbe> _logger;

    public BackendProbe(IHttpClientFactory httpClientFactory, ILogger<BackendProbe> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProbeOutcome> ProbeAsync(WatchdogTargetOptions target, CancellationToken cancellationToken)
    {
        WatchdogProbeOptions probe = target.Probe;
        long startedAt = Stopwatch.GetTimestamp();

        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, probe.TimeoutSeconds)));

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpRequestMessage request = new(HttpMethod.Get, probe.Url);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                budget.Token);

            string? body = null;
            if (!string.IsNullOrEmpty(probe.ExpectedBodyFragment))
            {
                body = await response.Content.ReadAsStringAsync(budget.Token);
            }

            long elapsed = ElapsedMilliseconds(startedAt);
            int statusCode = (int)response.StatusCode;

            if (!probe.ExpectedStatusCodes.Contains(statusCode))
            {
                string expected = string.Join(", ", probe.ExpectedStatusCodes);
                return ProbeOutcome.Unhealthy(target.Id, elapsed, $"HTTP {statusCode}, expected {expected}.", statusCode);
            }

            if (body is not null && !body.Contains(probe.ExpectedBodyFragment!, StringComparison.Ordinal))
            {
                return ProbeOutcome.Unhealthy(
                    target.Id,
                    elapsed,
                    $"HTTP {statusCode} but the body does not contain '{probe.ExpectedBodyFragment}'.",
                    statusCode);
            }

            if (probe.MaxLatencyMilliseconds is int ceiling && elapsed > ceiling)
            {
                return ProbeOutcome.Unhealthy(
                    target.Id,
                    elapsed,
                    $"answered HTTP {statusCode} but took {elapsed} ms, over the {ceiling} ms ceiling.",
                    statusCode);
            }

            return new ProbeOutcome
            {
                TargetId = target.Id,
                Healthy = true,
                StatusCode = statusCode,
                ElapsedMilliseconds = elapsed
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget expired, not the service shutting down. This is the case Apache's own
            // health checks cannot see, so it is the one that matters most here.
            return ProbeOutcome.Unhealthy(
                target.Id,
                ElapsedMilliseconds(startedAt),
                $"no complete response within {probe.TimeoutSeconds} s.");
        }
        catch (HttpRequestException exception)
        {
            return ProbeOutcome.Unhealthy(
                target.Id,
                ElapsedMilliseconds(startedAt),
                Describe(exception));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UriFormatException)
        {
            // A malformed URL is a configuration mistake, not a backend failure: say so loudly
            // rather than quietly taking a healthy server out of rotation over a typo.
            _logger.LogError(exception, "Target {TargetId} has an unusable probe URL '{Url}'.", target.Id, probe.Url);

            return ProbeOutcome.Unhealthy(
                target.Id,
                ElapsedMilliseconds(startedAt),
                $"misconfigured probe URL '{probe.Url}': {exception.Message}");
        }
    }

    private static long ElapsedMilliseconds(long startedAt)
    {
        return (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    private static string Describe(HttpRequestException exception)
    {
        // The inner exception carries the useful part (connection refused, TLS failure, DNS);
        // the outer message is usually a generic wrapper.
        string detail = exception.InnerException?.Message ?? exception.Message;
        return $"request failed: {detail}";
    }
}
