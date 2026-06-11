namespace Shared.Dtos;

/// <summary>Result of a successfully applied worker action.</summary>
public sealed class WorkerActionResultDto
{
    /// <summary>Identifier of the Apache server the action was applied on.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Balancer pool the worker belongs to.</summary>
    public string BalancerName { get; set; } = string.Empty;

    /// <summary>Worker URL the action targeted.</summary>
    public string WorkerUrl { get; set; } = string.Empty;

    /// <summary>Canonical name of the action that was applied.</summary>
    public string ActionApplied { get; set; } = string.Empty;

    /// <summary>Worker state scraped from Apache's response to the modification, proving the change took effect.</summary>
    public WorkerStatusDto? NewStatus { get; set; }
}
