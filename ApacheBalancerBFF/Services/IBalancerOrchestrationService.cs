using ApacheBalancerBFF.Domain;
using Shared.Dtos;

namespace ApacheBalancerBFF.Services;

/// <summary>High-level operations spanning all configured Apache servers.</summary>
public interface IBalancerOrchestrationService
{
    /// <summary>
    /// Queries every configured server concurrently and aggregates the results into a
    /// partial-success response: reachable servers in SuccessData, failures in Errors.
    /// </summary>
    Task<MultiServerResponseDto<ServerStatusDto>> GetClusterStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies a worker status change on one server: fetches a fresh nonce, validates the
    /// balancer/worker exist, submits the form POST and confirms the resulting state.
    /// </summary>
    Task<WorkerActionOutcome> ExecuteWorkerActionAsync(WorkerActionRequestDto request, CancellationToken cancellationToken);
}
