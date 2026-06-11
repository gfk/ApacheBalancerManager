using Shared.Dtos;

namespace ApacheBalancerWasmInterface.Models;

/// <summary>
/// Pool-first leaf: one worker row combined with the Apache server it was scraped from,
/// so the same worker URL can be compared side-by-side across servers.
/// </summary>
public sealed class WorkerViewModel
{
    /// <summary>Identifier of the Apache server this row came from (e.g. "lb-01").</summary>
    public string ServerId { get; init; } = string.Empty;

    /// <summary>Pool the worker belongs to; required to build a <see cref="WorkerActionRequestDto"/>.</summary>
    public string BalancerName { get; init; } = string.Empty;

    /// <summary>The underlying worker row as returned by the BFF.</summary>
    public WorkerStatusDto Worker { get; init; } = new WorkerStatusDto();

    public string Url => Worker.Url;

    public List<string> StatusFlags => Worker.StatusFlags;

    public string RawStatus => Worker.RawStatus;

    public string Load => Worker.Load;

    public string To => Worker.To;

    public string From => Worker.From;

    public string Elected => Worker.Elected;

    public string Busy => Worker.Busy;

    /// <summary>True when the worker carries the "Dis" (disabled) flag.</summary>
    public bool IsDisabled => Worker.StatusFlags.Contains("Dis");

    /// <summary>True when the worker carries the "Drn" (draining) flag.</summary>
    public bool IsDraining => Worker.StatusFlags.Contains("Drn");

    /// <summary>True when the worker carries the "Stby" (hot standby) flag.</summary>
    public bool IsStandby => Worker.StatusFlags.Contains("Stby");

    /// <summary>True when the worker is in an error-like state ("Err", "Stop" or "HcFl").</summary>
    public bool HasError => Worker.StatusFlags.Contains("Err")
        || Worker.StatusFlags.Contains("Stop")
        || Worker.StatusFlags.Contains("HcFl");
}
