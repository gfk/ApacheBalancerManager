using ApacheBalancerBFF.Configuration;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Optional source of byte-exact per-worker traffic counters: the JSON snapshot published by the
/// balancer-bytes aggregator on an Apache server (see tools/balancer-bytes-agg).
/// </summary>
public interface ITrafficMetricsClient
{
    /// <summary>
    /// Reads the traffic snapshot of one server, or returns null when the server has no
    /// <see cref="ApacheServerOptions.MetricsPath"/> configured, the endpoint could not be read, or
    /// the snapshot is too stale to trust.
    /// </summary>
    /// <remarks>
    /// Never throws. This is an enhancement over what the balancer-manager page already provides,
    /// so a broken or absent endpoint has to degrade to the scraped values rather than take the
    /// whole server's status down with it.
    /// </remarks>
    Task<TrafficSnapshot?> GetSnapshotAsync(ApacheServerOptions server, CancellationToken cancellationToken);
}
