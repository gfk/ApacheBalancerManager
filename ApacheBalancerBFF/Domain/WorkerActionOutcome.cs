using Shared.Dtos;

namespace ApacheBalancerBFF.Domain;

/// <summary>
/// Internal result of a worker action workflow, carrying either the success payload
/// or the HTTP status code (400/502) and reason the controller should surface.
/// </summary>
public sealed class WorkerActionOutcome
{
    /// <summary>True when the action was submitted to Apache successfully.</summary>
    public bool Succeeded { get; private init; }

    /// <summary>HTTP status code to return on failure (400 for validation, 502 for upstream errors).</summary>
    public int? FailureStatusCode { get; private init; }

    /// <summary>Human-readable failure reason.</summary>
    public string? FailureReason { get; private init; }

    /// <summary>Success payload, set only when <see cref="Succeeded"/> is true.</summary>
    public WorkerActionResultDto? Result { get; private init; }

    /// <summary>Creates a successful outcome.</summary>
    public static WorkerActionOutcome Success(WorkerActionResultDto result)
    {
        return new WorkerActionOutcome { Succeeded = true, Result = result };
    }

    /// <summary>Creates a failed outcome with the HTTP status code and reason to surface.</summary>
    public static WorkerActionOutcome Failure(int statusCode, string reason)
    {
        return new WorkerActionOutcome { Succeeded = false, FailureStatusCode = statusCode, FailureReason = reason };
    }
}
