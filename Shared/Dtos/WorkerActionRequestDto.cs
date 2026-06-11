using System.ComponentModel.DataAnnotations;

namespace Shared.Dtos;

/// <summary>Request to change the status of one worker on one Apache server.</summary>
public sealed class WorkerActionRequestDto
{
    /// <summary>Configured identifier of the target Apache server (e.g. "lb-01").</summary>
    [Required]
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Balancer pool containing the worker (e.g. "web-public-backend"). Required because the same worker URL can belong to several pools.</summary>
    [Required]
    public string BalancerName { get; set; } = string.Empty;

    /// <summary>Worker URL exactly as displayed by balancer-manager (e.g. "https://web-public.app-1.example.com").</summary>
    [Required]
    public string WorkerUrl { get; set; } = string.Empty;

    /// <summary>Action to apply, case-insensitive. One of: Enable, Disable, Drain, Stop, DisableOff, DrainOff, StopOff, IgnoreErrorsOn, IgnoreErrorsOff, HotStandbyOn, HotStandbyOff, HotSpareOn, HotSpareOff, HcFailOn, HcFailOff.</summary>
    [Required]
    public string ActionType { get; set; } = string.Empty;
}
