namespace ApacheBalancerBFF.Domain;

/// <summary>Result of one watchdog probe against one backend.</summary>
public sealed record ProbeOutcome
{
    /// <summary>Identifier of the target that was probed.</summary>
    public required string TargetId { get; init; }

    /// <summary>True when the response arrived in time, with an expected status and body.</summary>
    public required bool Healthy { get; init; }

    /// <summary>Status code received, or null when nothing came back.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Time the exchange took, including a timeout that cut it short.</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>Why the probe is considered unhealthy; null when it passed.</summary>
    public string? Failure { get; init; }

    /// <summary>When the probe completed.</summary>
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Builds a failed outcome.</summary>
    public static ProbeOutcome Unhealthy(string targetId, long elapsedMilliseconds, string failure, int? statusCode = null)
    {
        return new ProbeOutcome
        {
            TargetId = targetId,
            Healthy = false,
            StatusCode = statusCode,
            ElapsedMilliseconds = elapsedMilliseconds,
            Failure = failure
        };
    }
}
