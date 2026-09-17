namespace ApacheBalancerBFF.Configuration;

/// <summary>
/// A single Apache HTTPD instance running mod_proxy_balancer with /balancer-manager enabled.
/// </summary>
public sealed class ApacheServerOptions
{
    /// <summary>Stable identifier used by API clients to target this server (e.g. "lb-01").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-friendly display name (e.g. "Load Balancer Primary").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Base URL of the Apache instance, without the /balancer-manager path (e.g. "http://10.0.0.11").</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional path, relative to <see cref="BaseUrl"/>, of the byte-exact traffic snapshot published
    /// by the balancer-bytes aggregator (e.g. "/balancer-bytes"). Leave it unset — the default — and
    /// the server is read from its balancer-manager page alone, exactly as before.
    /// </summary>
    public string? MetricsPath { get; set; }
}
