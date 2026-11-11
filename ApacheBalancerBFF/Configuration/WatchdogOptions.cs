namespace ApacheBalancerBFF.Configuration;

/// <summary>
/// Settings of the external watchdog: a supervisor that probes the backends itself and takes a
/// failing one out of rotation through /balancer-manager, instead of waiting for Apache's own
/// health checks to notice.
/// </summary>
/// <remarks>
/// It exists because mod_proxy_hcheck has no probe timeout of its own: a health check shares the
/// worker's <c>timeout=</c> with real user traffic. On a backend whose slowest legitimate pages
/// take minutes, that timeout has to stay high, and a probe sent to a backend that accepts
/// connections but never answers simply hangs. A probe that never completes never counts as a
/// failure, so the member is never demoted. This watchdog has no such constraint: its budget is
/// a few seconds and applies to probes only.
/// </remarks>
public sealed class WatchdogOptions
{
    /// <summary>Name of the configuration section in appsettings.json.</summary>
    public const string SectionName = "Watchdog";

    /// <summary>Whether the background loop runs at all. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Observe-only mode: probe, decide and log exactly as in armed mode, but never send anything
    /// to Apache. On by default, and the only responsible way to start: run it for a few weeks and
    /// check that it would have fired on the real outages and stayed silent the rest of the time.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Delay between two probe cycles, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>Consecutive failed probes required before a worker is disabled.</summary>
    public int FailuresBeforeDisable { get; set; } = 3;

    /// <summary>
    /// Consecutive successful probes required before a worker the watchdog disabled is put back.
    /// Deliberately larger than <see cref="FailuresBeforeDisable"/>: coming back is less urgent
    /// than leaving, and a backend that recovers then relapses should not flap.
    /// </summary>
    public int SuccessesBeforeEnable { get; set; } = 6;

    /// <summary>
    /// Hard ceiling on the number of worker actions in any rolling hour. Once reached, the
    /// watchdog refuses to act and says so in the log: past that rate, something is wrong with the
    /// watchdog's own judgement and a human should look before more backends are pulled out.
    /// </summary>
    public int MaxActionsPerHour { get; set; } = 6;

    /// <summary>
    /// File where the watchdog records which workers it disabled, so a restart does not strand
    /// them. Defaults to "watchdog-state.json" next to the binaries; in a container, point it at
    /// a mounted volume. A worker absent from this file is never re-enabled: if a human disabled
    /// it by hand, the watchdog leaves it alone.
    /// </summary>
    public string? StateFilePath { get; set; }

    /// <summary>
    /// PEM or DER certificate to validate the backends' TLS chain against, for the common case
    /// where they are re-signed on the fly by a firewall doing deep inspection and that CA is not
    /// in the host's trust store. When null, the usual system validation applies.
    /// </summary>
    public string? CustomRootCertificatePath { get; set; }

    /// <summary>What to probe, and which worker each probe speaks for.</summary>
    public List<WatchdogTargetOptions> Targets { get; set; } = new();
}

/// <summary>
/// One supervised unit: a probe, and the balancer worker that probe decides about. The same
/// worker URL is acted on across every server of "ApacheManagement:Servers", all or none.
/// </summary>
public sealed class WatchdogTargetOptions
{
    /// <summary>Identifier used in logs and to resolve <see cref="PeerTargetId"/> (e.g. "web-public@app-1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The target serving the same site on the other backend. When set, this one is never disabled
    /// unless its peer's last probe passed. Two sick backends mean the fault is somewhere they
    /// share — the database, the network, a bad release — and moving traffic between them fixes
    /// nothing while cutting every request in flight.
    /// </summary>
    public string? PeerTargetId { get; set; }

    /// <summary>How to tell whether this backend is serving.</summary>
    public WatchdogProbeOptions Probe { get; set; } = new();

    /// <summary>Balancer pool holding the worker, as /balancer-manager names it.</summary>
    public string BalancerName { get; set; } = string.Empty;

    /// <summary>Worker URL exactly as /balancer-manager displays it.</summary>
    public string WorkerUrl { get; set; } = string.Empty;
}

/// <summary>A single HTTP check, with a budget that belongs to the watchdog alone.</summary>
public sealed class WatchdogProbeOptions
{
    /// <summary>Absolute URL of the backend to probe, bypassing the proxies.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Time allowed for the whole exchange. This is the point of the watchdog: keep it small.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Optional latency ceiling, below the timeout. Lets a backend that still answers but has
    /// become unusably slow be treated as unhealthy before it stops answering altogether.
    /// </summary>
    public int? MaxLatencyMilliseconds { get; set; }

    /// <summary>Status codes that count as healthy.</summary>
    public List<int> ExpectedStatusCodes { get; set; } = new() { 200 };

    /// <summary>
    /// Optional text the response body must contain. Worth setting: it is what separates a page
    /// the application actually produced from a holding page served by the web server itself.
    /// </summary>
    public string? ExpectedBodyFragment { get; set; }
}
