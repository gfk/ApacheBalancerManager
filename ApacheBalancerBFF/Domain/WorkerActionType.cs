namespace ApacheBalancerBFF.Domain;

/// <summary>
/// Worker state changes supported by the BFF, mapped onto the seven status flags
/// exposed by the Apache balancer-manager edit form (w_status_I/N/D/H/R/C/S).
/// </summary>
public enum WorkerActionType
{
    /// <summary>Convenience action: clears Disabled, Drain and Stopped in one call (w_status_D=0, w_status_N=0, w_status_S=0).</summary>
    Enable,

    /// <summary>Sets the Disabled flag (w_status_D=1).</summary>
    Disable,

    /// <summary>Sets the Draining Mode flag (w_status_N=1).</summary>
    Drain,

    /// <summary>Sets the Stopped flag (w_status_S=1).</summary>
    Stop,

    /// <summary>Clears the Disabled flag (w_status_D=0).</summary>
    DisableOff,

    /// <summary>Clears the Draining Mode flag (w_status_N=0).</summary>
    DrainOff,

    /// <summary>Clears the Stopped flag (w_status_S=0).</summary>
    StopOff,

    /// <summary>Sets the Ignore Errors flag (w_status_I=1).</summary>
    IgnoreErrorsOn,

    /// <summary>Clears the Ignore Errors flag (w_status_I=0).</summary>
    IgnoreErrorsOff,

    /// <summary>Sets the Hot Standby flag (w_status_H=1).</summary>
    HotStandbyOn,

    /// <summary>Clears the Hot Standby flag (w_status_H=0).</summary>
    HotStandbyOff,

    /// <summary>Sets the Hot Spare flag (w_status_R=1).</summary>
    HotSpareOn,

    /// <summary>Clears the Hot Spare flag (w_status_R=0).</summary>
    HotSpareOff,

    /// <summary>Sets the health-check failure flag (w_status_C=1).</summary>
    HcFailOn,

    /// <summary>Clears the health-check failure flag (w_status_C=0).</summary>
    HcFailOff
}
