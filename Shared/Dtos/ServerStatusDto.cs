namespace Shared.Dtos;

/// <summary>Scraped balancer-manager state of one Apache server.</summary>
public sealed class ServerStatusDto
{
    /// <summary>Configured identifier of the server (e.g. "lb-01").</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Configured display name of the server.</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Configured base URL of the server.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>All balancer pools found on the server's /balancer-manager page.</summary>
    public List<BalancerStatusDto> Balancers { get; set; } = new();
}
