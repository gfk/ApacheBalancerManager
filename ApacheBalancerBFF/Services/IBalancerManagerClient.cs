using ApacheBalancerBFF.Configuration;

namespace ApacheBalancerBFF.Services;

/// <summary>Raw HTTP access to an Apache server's /balancer-manager handler.</summary>
public interface IBalancerManagerClient
{
    /// <summary>
    /// Fetches the balancer-manager HTML status page of the given server.
    /// Throws <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/> on failure.
    /// </summary>
    Task<string> GetStatusPageAsync(ApacheServerOptions server, CancellationToken cancellationToken);

    /// <summary>
    /// Submits an application/x-www-form-urlencoded modification request to the given server's
    /// balancer-manager handler and returns the refreshed HTML page Apache responds with.
    /// Throws <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/> on failure.
    /// </summary>
    Task<string> PostWorkerFormAsync(ApacheServerOptions server, IReadOnlyDictionary<string, string> formFields, CancellationToken cancellationToken);
}
