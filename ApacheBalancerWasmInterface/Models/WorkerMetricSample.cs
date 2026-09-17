namespace ApacheBalancerWasmInterface.Models;

/// <summary>
/// The four plotted counters of one worker, as read at a single poll. <paramref name="PreciseBytes"/>
/// records where the byte counters came from: the server's balancer-bytes endpoint, which is exact,
/// or the balancer-manager cells, which Apache has rounded to three significant characters. The rate
/// windows differ by a factor of ten between the two, so the distinction has to travel with the sample.
/// </summary>
public sealed record WorkerMetricSample(
    DateTime Timestamp,
    double Elected,
    double Busy,
    double ToBytes,
    double FromBytes,
    bool PreciseBytes);

/// <summary>One point of a plotted line.</summary>
public sealed record MetricPoint(DateTime Timestamp, double Value);

/// <summary>
/// The recorded history of one worker inside a pool, together with the categorical colour slot
/// it was given. The slot is assigned once, when the worker is first seen, so a worker keeps its
/// colour even if other workers of the pool come and go.
/// </summary>
public sealed class TrackedWorker
{
    public string Key { get; init; } = string.Empty;

    public string ServerId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    /// <summary>Index into the categorical palette; workers past the palette share the "Other" line.</summary>
    public int ColorSlot { get; init; }

    public IReadOnlyList<WorkerMetricSample> Samples { get; init; } = Array.Empty<WorkerMetricSample>();

    /// <summary>Legend label: the worker's host, qualified by the Apache server it was scraped from.</summary>
    public string Label
    {
        get
        {
            int schemeIndex = Url.IndexOf("://", StringComparison.Ordinal);
            string host = schemeIndex >= 0 ? Url[(schemeIndex + 3)..] : Url;
            return $"{host} @ {ServerId}";
        }
    }
}
