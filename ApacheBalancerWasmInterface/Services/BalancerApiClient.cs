using System.Net.Http.Json;
using Shared.Dtos;

namespace ApacheBalancerWasmInterface.Services;

/// <summary>Typed HTTP client for the Apache Balancer BFF.</summary>
public sealed class BalancerApiClient
{
    private readonly HttpClient httpClient;

    public BalancerApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <summary>Fetches the balancer and worker status of every configured Apache server.</summary>
    public async Task<MultiServerResponseDto<ServerStatusDto>?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MultiServerResponseDto<ServerStatusDto>>(
            "api/balancer/status", cancellationToken);
    }

    /// <summary>Applies a status-changing action to a single worker on a single server.</summary>
    public async Task<WorkerActionResultDto?> PostWorkerActionAsync(
        WorkerActionRequestDto request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "api/balancer/worker/status", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ProblemDetailsPayload? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(cancellationToken);
            }
            catch (Exception)
            {
                // Non-JSON error body; fall back to the status code message below.
            }

            string message = problem?.Detail ?? problem?.Title ?? $"The BFF returned HTTP {(int)response.StatusCode}.";
            throw new BalancerApiException(message);
        }

        return await response.Content.ReadFromJsonAsync<WorkerActionResultDto>(cancellationToken);
    }

    private sealed class ProblemDetailsPayload
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }
    }
}
