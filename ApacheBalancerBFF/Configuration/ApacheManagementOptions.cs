namespace ApacheBalancerBFF.Configuration;

/// <summary>
/// Root configuration section describing the Apache HTTPD servers managed by this BFF.
/// Bound from the "ApacheManagement" section of appsettings.json.
/// </summary>
public sealed class ApacheManagementOptions
{
    /// <summary>Name of the configuration section in appsettings.json.</summary>
    public const string SectionName = "ApacheManagement";

    /// <summary>The Apache HTTPD instances exposing /balancer-manager.</summary>
    public List<ApacheServerOptions> Servers { get; set; } = new();
}
