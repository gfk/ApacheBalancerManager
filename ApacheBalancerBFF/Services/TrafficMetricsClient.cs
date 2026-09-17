using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApacheBalancerBFF.Configuration;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Default <see cref="ITrafficMetricsClient"/>: fetches and validates the balancer-bytes JSON
/// snapshot of a server over HTTP.
/// </summary>
public sealed class TrafficMetricsClient : ITrafficMetricsClient
{
    /// <summary>Name of the named HttpClient registered in Program.cs.</summary>
    public const string HttpClientName = "ApacheTrafficMetrics";

    /// <summary>The only snapshot layout this build understands; see tools/balancer-bytes-agg.</summary>
    private const int SupportedSnapshotVersion = 1;

    /// <summary>
    /// How stale a snapshot may be before it is ignored. The aggregator republishes every second
    /// even when no request came in, so anything older than this means it died or was never fed —
    /// and a frozen counter would be plotted as "no traffic", which is worse than falling back to
    /// Apache's rounded cells.
    /// </summary>
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TrafficMetricsClient> _logger;

    /// <summary>
    /// Last failure reported per server. This client is a singleton purely to hold it: the dashboard
    /// polls as fast as once a second, and a misconfigured endpoint would otherwise fill the log with
    /// the same line thousands of times an hour instead of once.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _lastFailureByServer =
        new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public TrafficMetricsClient(IHttpClientFactory httpClientFactory, ILogger<TrafficMetricsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrafficSnapshot?> GetSnapshotAsync(ApacheServerOptions server, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.MetricsPath))
        {
            return null;
        }

        string url = BuildMetricsUrl(server);

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            SnapshotDocument? document = await JsonSerializer
                .DeserializeAsync<SnapshotDocument>(stream, SnapshotSerializerOptions, cancellationToken);

            if (document is null)
            {
                return ReportFailure(server, url, "the endpoint returned an empty document");
            }

            if (document.Version != SupportedSnapshotVersion)
            {
                return ReportFailure(server, url,
                    $"the snapshot claims version {document.Version}, and this build understands version {SupportedSnapshotVersion}");
            }

            if (document.Generated == default)
            {
                return ReportFailure(server, url, "the snapshot carries no 'generated' timestamp");
            }

            // Age is measured against Apache's own clock, taken from the response Date header, so a
            // few seconds of drift between this host and the load balancer cannot age a perfectly
            // fresh snapshot out of existence.
            DateTimeOffset readAt = response.Headers.Date ?? DateTimeOffset.UtcNow;
            TimeSpan age = readAt - document.Generated;
            if (age > MaximumSnapshotAge)
            {
                return ReportFailure(server, url,
                    $"the snapshot is {age.TotalSeconds:F0}s old, so the aggregator is no longer publishing; " +
                    "check the Apache error log for balancer-bytes-agg");
            }

            // An absent worker is read as zero bytes downstream, which is only sound if the pipeline is
            // actually being fed. A snapshot with no rows at all proves the opposite, so it is treated
            // as unusable: a brand-new aggregator costs nothing by waiting, and a pipeline that is
            // never fed says so here instead of confidently plotting no traffic on every worker.
            if (document.Workers.Count == 0)
            {
                return ReportFailure(server, url,
                    "the snapshot is empty, so no balancer-proxied request has reached the aggregator yet; " +
                    "if that persists, check that the CustomLog line is in the vhost carrying the balancer traffic");
            }

            ReportSuccess(server, url, document.Workers.Count);

            return TrafficSnapshot.FromRows(document.Workers
                .Select((SnapshotWorker worker) => (worker.Balancer, worker.Worker, worker.To, worker.From)));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          or JsonException or UriFormatException or InvalidOperationException
                                          or NotSupportedException)
        {
            // A cancelled poll is the caller going away, not a broken endpoint.
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return ReportFailure(server, url, DescribeException(exception));
        }
    }

    private TrafficSnapshot? ReportFailure(ApacheServerOptions server, string url, string reason)
    {
        if (_lastFailureByServer.TryGetValue(server.Id, out string? previousReason) && previousReason == reason)
        {
            return null;
        }

        _lastFailureByServer[server.Id] = reason;
        _logger.LogWarning(
            "Precise traffic counters are unavailable for server {ServerId} ({Url}): {Reason}. " +
            "Falling back to the rounded To/From cells of the balancer-manager page.",
            server.Id,
            url,
            reason);

        return null;
    }

    private void ReportSuccess(ApacheServerOptions server, string url, int workerCount)
    {
        if (_lastFailureByServer.TryRemove(server.Id, out _))
        {
            _logger.LogInformation(
                "Precise traffic counters are available again for server {ServerId} ({Url}): {WorkerCount} workers.",
                server.Id,
                url,
                workerCount);
        }
    }

    private static string BuildMetricsUrl(ApacheServerOptions server)
    {
        string path = server.MetricsPath!.Trim();
        return server.BaseUrl.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
    }

    private static string DescribeException(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return "the request timed out";
        }

        if (exception is HttpRequestException httpException && httpException.StatusCode is not null)
        {
            return $"the endpoint returned HTTP {(int)httpException.StatusCode} ({httpException.StatusCode})";
        }

        if (exception is JsonException jsonException)
        {
            return $"the response is not a valid snapshot ({jsonException.Message})";
        }

        return exception.InnerException?.Message ?? exception.Message;
    }

    /// <summary>The snapshot document as tools/balancer-bytes-agg writes it.</summary>
    private sealed class SnapshotDocument
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("generated")]
        public DateTimeOffset Generated { get; set; }

        [JsonPropertyName("workers")]
        public List<SnapshotWorker> Workers { get; set; } = new List<SnapshotWorker>();
    }

    private sealed class SnapshotWorker
    {
        /// <summary>Apache's BALANCER_NAME, i.e. "balancer://web-public-backend".</summary>
        [JsonPropertyName("balancer")]
        public string Balancer { get; set; } = string.Empty;

        /// <summary>Apache's BALANCER_WORKER_NAME, which matches the manager page's worker URL.</summary>
        [JsonPropertyName("worker")]
        public string Worker { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public long To { get; set; }

        [JsonPropertyName("from")]
        public long From { get; set; }
    }
}
