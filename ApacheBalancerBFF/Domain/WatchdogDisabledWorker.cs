namespace ApacheBalancerBFF.Domain;

/// <summary>
/// One worker the watchdog disabled, on one Apache server. Persisted so that a restart of this
/// service does not leave a backend out of rotation with nobody left to put it back.
/// </summary>
/// <remarks>
/// This record is also what keeps the watchdog from touching anybody else's work: Apache's status
/// flags carry no provenance, so a worker a human disabled by hand looks exactly like one the
/// watchdog disabled. Only workers listed here are ever re-enabled.
/// </remarks>
public sealed class WatchdogDisabledWorker
{
    /// <summary>Target the action was taken for.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Apache server the worker was disabled on.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Balancer pool holding the worker.</summary>
    public string BalancerName { get; set; } = string.Empty;

    /// <summary>Worker URL as /balancer-manager displays it.</summary>
    public string WorkerUrl { get; set; } = string.Empty;

    /// <summary>When the watchdog disabled it.</summary>
    public DateTimeOffset DisabledAt { get; set; }

    /// <summary>Probe failure that triggered the action, kept for the post-mortem.</summary>
    public string Reason { get; set; } = string.Empty;
}
