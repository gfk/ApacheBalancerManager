namespace Shared.Dtos;

/// <summary>One balancer pool (balancer://name) scraped from a balancer-manager page.</summary>
public sealed class BalancerStatusDto
{
    /// <summary>Pool name without the "balancer://" scheme or the per-process prefix (e.g. "web-public-backend").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Per-balancer nonce required by Apache to accept modification requests.</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Raw "MaxMembers" cell (e.g. "2 [2 Used]").</summary>
    public string MaxMembers { get; set; } = string.Empty;

    /// <summary>Sticky session configuration ("(None)" when disabled).</summary>
    public string StickySession { get; set; } = string.Empty;

    /// <summary>Whether failover is disabled ("On"/"Off").</summary>
    public string DisableFailover { get; set; } = string.Empty;

    /// <summary>Balancer timeout.</summary>
    public string Timeout { get; set; } = string.Empty;

    /// <summary>Number of failover attempts.</summary>
    public string FailoverAttempts { get; set; } = string.Empty;

    /// <summary>Load-balancing method (e.g. "byrequests", "bybusyness").</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Mount path of the balancer.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether the balancer is active ("Yes"/"No").</summary>
    public string Active { get; set; } = string.Empty;

    /// <summary>The pool's backend workers.</summary>
    public List<WorkerStatusDto> Workers { get; set; } = new();
}
