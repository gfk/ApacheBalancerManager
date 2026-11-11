using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Domain;

namespace ApacheBalancerBFF.Services;

/// <summary>Performs the watchdog's own health check against one backend.</summary>
public interface IBackendProbe
{
    /// <summary>
    /// Probes the target and reports what happened. Never throws for a backend-side problem:
    /// an unreachable, slow or wrong-answering backend comes back as an unhealthy outcome, so
    /// one bad target cannot abort a whole cycle.
    /// </summary>
    Task<ProbeOutcome> ProbeAsync(WatchdogTargetOptions target, CancellationToken cancellationToken);
}
