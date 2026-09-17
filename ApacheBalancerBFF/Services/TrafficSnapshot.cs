namespace ApacheBalancerBFF.Services;

/// <summary>Byte totals one worker has carried since the aggregator started.</summary>
public readonly record struct WorkerTrafficCounters(long ToBytes, long FromBytes);

/// <summary>
/// One reading of a server's balancer-bytes endpoint: the exact byte totals of every worker the
/// aggregator has seen traffic for, indexed the way the balancer-manager page names them.
/// </summary>
public sealed class TrafficSnapshot
{
    private const string BalancerScheme = "balancer://";

    private readonly Dictionary<string, WorkerTrafficCounters> _countersByWorker;

    private TrafficSnapshot(Dictionary<string, WorkerTrafficCounters> countersByWorker)
    {
        _countersByWorker = countersByWorker;
    }

    /// <summary>Number of workers the snapshot carries counters for.</summary>
    public int WorkerCount => _countersByWorker.Count;

    /// <summary>
    /// Builds a snapshot from the aggregator's rows. The balancer name arrives as Apache's
    /// BALANCER_NAME ("balancer://web-public-backend"); the manager page prints it without the
    /// scheme, so the prefix is dropped here to make the two join.
    /// </summary>
    public static TrafficSnapshot FromRows(IEnumerable<(string Balancer, string Worker, long ToBytes, long FromBytes)> rows)
    {
        Dictionary<string, WorkerTrafficCounters> countersByWorker =
            new Dictionary<string, WorkerTrafficCounters>(StringComparer.OrdinalIgnoreCase);

        foreach ((string balancer, string worker, long toBytes, long fromBytes) in rows)
        {
            if (string.IsNullOrWhiteSpace(balancer) || string.IsNullOrWhiteSpace(worker))
            {
                continue;
            }

            countersByWorker[BuildKey(StripBalancerScheme(balancer), worker)] =
                new WorkerTrafficCounters(toBytes, fromBytes);
        }

        return new TrafficSnapshot(countersByWorker);
    }

    /// <summary>
    /// Counters of one worker, or false when the aggregator has not seen a request for it — a
    /// worker that has been idle since Apache started simply is not in the snapshot yet.
    /// </summary>
    public bool TryGetCounters(string balancerName, string workerUrl, out WorkerTrafficCounters counters)
    {
        return _countersByWorker.TryGetValue(BuildKey(balancerName, workerUrl), out counters);
    }

    private static string BuildKey(string balancerName, string workerUrl)
    {
        return balancerName + "|" + workerUrl;
    }

    private static string StripBalancerScheme(string balancerName)
    {
        return balancerName.StartsWith(BalancerScheme, StringComparison.OrdinalIgnoreCase)
            ? balancerName[BalancerScheme.Length..]
            : balancerName;
    }
}
