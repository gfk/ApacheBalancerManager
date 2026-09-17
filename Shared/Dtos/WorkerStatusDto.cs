namespace Shared.Dtos;

/// <summary>One backend worker row scraped from a balancer pool's worker table.</summary>
public sealed class WorkerStatusDto
{
    /// <summary>Worker URL exactly as Apache displays it (e.g. "https://10.0.0.23").</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Session route of the worker.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>Route redirection target.</summary>
    public string RouteRedir { get; set; } = string.Empty;

    /// <summary>Load factor (the "Factor" column), e.g. 1.00.</summary>
    public double LoadFactor { get; set; }

    /// <summary>Load-balancer set ("Set" column).</summary>
    public string Set { get; set; } = string.Empty;

    /// <summary>Raw status text as displayed by Apache (e.g. "Init Stby Ok").</summary>
    public string RawStatus { get; set; } = string.Empty;

    /// <summary>Individual status flags split from <see cref="RawStatus"/> (e.g. ["Init","Stby","Ok"]). Known tokens include Init, Ok, Dis, Drn, Stby, Spar, Ign, Stop, Err, HcFl.</summary>
    public List<string> StatusFlags { get; set; } = new();

    /// <summary>Number of times the worker was elected.</summary>
    public string Elected { get; set; } = string.Empty;

    /// <summary>Current busy count.</summary>
    public string Busy { get; set; } = string.Empty;

    /// <summary>Current load score.</summary>
    public string Load { get; set; } = string.Empty;

    /// <summary>Bytes sent to the worker ("To" column).</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Bytes received from the worker ("From" column).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Exact bytes sent to the worker, counted from the access log by the optional balancer-bytes
    /// aggregator; null when the server has no metrics endpoint configured or it could not be read.
    /// </summary>
    /// <remarks>
    /// This is a number rather than a verbatim cell because its source is one: the aggregator
    /// publishes raw byte totals, so nothing is being rounded back up here. <see cref="To"/> stays
    /// exactly as Apache rendered it either way.
    /// </remarks>
    public long? ToBytes { get; set; }

    /// <summary>
    /// Exact bytes received from the worker, counted from the access log by the optional
    /// balancer-bytes aggregator; null when it is not configured or could not be read.
    /// </summary>
    public long? FromBytes { get; set; }

    /// <summary>Health-check method (e.g. "GET", "NONE"). Empty when the column is absent.</summary>
    public string HealthCheckMethod { get; set; } = string.Empty;

    /// <summary>Health-check interval (e.g. "5000ms").</summary>
    public string HealthCheckInterval { get; set; } = string.Empty;

    /// <summary>Health-check passes trigger (e.g. "2 (0)").</summary>
    public string HealthCheckPasses { get; set; } = string.Empty;

    /// <summary>Health-check fails trigger (e.g. "4 (0)").</summary>
    public string HealthCheckFails { get; set; } = string.Empty;

    /// <summary>Health-check URI.</summary>
    public string HealthCheckUri { get; set; } = string.Empty;

    /// <summary>Health-check expression.</summary>
    public string HealthCheckExpr { get; set; } = string.Empty;
}
