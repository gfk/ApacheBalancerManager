namespace ApacheBalancerBFF.Domain;

/// <summary>
/// Translates a <see cref="WorkerActionType"/> into the exact form fields the Apache
/// balancer-manager POST handler expects. mod_proxy_balancer only applies the parameters
/// present in the request body, so each action sends the minimal set of fields.
/// </summary>
public static class WorkerActionMapper
{
    private const string IgnoreErrorsField = "w_status_I";
    private const string DrainField = "w_status_N";
    private const string DisabledField = "w_status_D";
    private const string HotStandbyField = "w_status_H";
    private const string HotSpareField = "w_status_R";
    private const string HcFailField = "w_status_C";
    private const string StoppedField = "w_status_S";

    /// <summary>Returns the status form fields (name → "0"/"1") for the given action.</summary>
    public static IReadOnlyDictionary<string, string> ToFormFields(WorkerActionType actionType)
    {
        Dictionary<string, string> fields = actionType switch
        {
            WorkerActionType.Enable => new Dictionary<string, string>
            {
                [DisabledField] = "0",
                [DrainField] = "0",
                [StoppedField] = "0"
            },
            WorkerActionType.Disable => new Dictionary<string, string> { [DisabledField] = "1" },
            WorkerActionType.DisableOff => new Dictionary<string, string> { [DisabledField] = "0" },
            WorkerActionType.Drain => new Dictionary<string, string> { [DrainField] = "1" },
            WorkerActionType.DrainOff => new Dictionary<string, string> { [DrainField] = "0" },
            WorkerActionType.Stop => new Dictionary<string, string> { [StoppedField] = "1" },
            WorkerActionType.StopOff => new Dictionary<string, string> { [StoppedField] = "0" },
            WorkerActionType.IgnoreErrorsOn => new Dictionary<string, string> { [IgnoreErrorsField] = "1" },
            WorkerActionType.IgnoreErrorsOff => new Dictionary<string, string> { [IgnoreErrorsField] = "0" },
            WorkerActionType.HotStandbyOn => new Dictionary<string, string> { [HotStandbyField] = "1" },
            WorkerActionType.HotStandbyOff => new Dictionary<string, string> { [HotStandbyField] = "0" },
            WorkerActionType.HotSpareOn => new Dictionary<string, string> { [HotSpareField] = "1" },
            WorkerActionType.HotSpareOff => new Dictionary<string, string> { [HotSpareField] = "0" },
            WorkerActionType.HcFailOn => new Dictionary<string, string> { [HcFailField] = "1" },
            WorkerActionType.HcFailOff => new Dictionary<string, string> { [HcFailField] = "0" },
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported worker action.")
        };

        return fields;
    }

    /// <summary>
    /// Returns the status-flag tokens (as rendered by Apache, e.g. "Stby") that the worker's
    /// post-action status must (true) or must not (false) contain for the action to be
    /// considered applied. Used to detect Apache silently ignoring a modification request.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ToExpectedFlagStates(WorkerActionType actionType)
    {
        Dictionary<string, bool> expectations = actionType switch
        {
            WorkerActionType.Enable => new Dictionary<string, bool>
            {
                ["Dis"] = false,
                ["Drn"] = false,
                ["Stop"] = false
            },
            WorkerActionType.Disable => new Dictionary<string, bool> { ["Dis"] = true },
            WorkerActionType.DisableOff => new Dictionary<string, bool> { ["Dis"] = false },
            WorkerActionType.Drain => new Dictionary<string, bool> { ["Drn"] = true },
            WorkerActionType.DrainOff => new Dictionary<string, bool> { ["Drn"] = false },
            WorkerActionType.Stop => new Dictionary<string, bool> { ["Stop"] = true },
            WorkerActionType.StopOff => new Dictionary<string, bool> { ["Stop"] = false },
            WorkerActionType.IgnoreErrorsOn => new Dictionary<string, bool> { ["Ign"] = true },
            WorkerActionType.IgnoreErrorsOff => new Dictionary<string, bool> { ["Ign"] = false },
            WorkerActionType.HotStandbyOn => new Dictionary<string, bool> { ["Stby"] = true },
            WorkerActionType.HotStandbyOff => new Dictionary<string, bool> { ["Stby"] = false },
            WorkerActionType.HotSpareOn => new Dictionary<string, bool> { ["Spar"] = true },
            WorkerActionType.HotSpareOff => new Dictionary<string, bool> { ["Spar"] = false },
            WorkerActionType.HcFailOn => new Dictionary<string, bool> { ["HcFl"] = true },
            WorkerActionType.HcFailOff => new Dictionary<string, bool> { ["HcFl"] = false },
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported worker action.")
        };

        return expectations;
    }
}
