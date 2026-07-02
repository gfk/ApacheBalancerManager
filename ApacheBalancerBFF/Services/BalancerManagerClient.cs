using ApacheBalancerBFF.Configuration;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Default <see cref="IBalancerManagerClient"/> backed by <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class BalancerManagerClient : IBalancerManagerClient
{
    /// <summary>Name of the named HttpClient registered in Program.cs.</summary>
    public const string HttpClientName = "ApacheBalancerManager";

    private const string ManagerPath = "/balancer-manager";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BalancerManagerClient> _logger;

    public BalancerManagerClient(IHttpClientFactory httpClientFactory, ILogger<BalancerManagerClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetStatusPageAsync(ApacheServerOptions server, CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        string url = BuildManagerUrl(server);

        _logger.LogDebug("GET {Url} (server {ServerId})", url, server.Id);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        // Apache 2.4's balancer-manager cross-site check (AH10187) rejects any request
        // whose Referer is not the manager page itself. Without it Apache logs a warning
        // on every status poll; set the Referer to the manager URL to silence it.
        request.Headers.Referrer = new Uri(url);

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        return html;
    }

    /// <inheritdoc />
    public async Task<string> PostWorkerFormAsync(ApacheServerOptions server, IReadOnlyDictionary<string, string> formFields, CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        string url = BuildManagerUrl(server);

        _logger.LogInformation(
            "POST {Url} (server {ServerId}) with fields: {Fields}",
            url,
            server.Id,
            string.Join(", ", formFields.Select((KeyValuePair<string, string> pair) => $"{pair.Key}={pair.Value}")));

        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(formFields)
        };
        // Apache 2.4's balancer-manager CSRF protection silently ignores modification
        // requests (HTTP 200, no change applied) unless the Referer is the manager page.
        request.Headers.Referrer = new Uri(url);

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        return html;
    }

    private static string BuildManagerUrl(ApacheServerOptions server)
    {
        return server.BaseUrl.TrimEnd('/') + ManagerPath;
    }
}
