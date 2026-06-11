using ApacheBalancerBFF.Domain;
using Shared.Dtos;
using ApacheBalancerBFF.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApacheBalancerBFF.Controllers;

/// <summary>
/// Unified REST facade over the /balancer-manager pages of all configured Apache servers.
/// </summary>
[ApiController]
[Route("api/balancer")]
[Produces("application/json")]
public sealed class BalancerController : ControllerBase
{
    private readonly IBalancerOrchestrationService _orchestrationService;
    private readonly ILogger<BalancerController> _logger;

    public BalancerController(IBalancerOrchestrationService orchestrationService, ILogger<BalancerController> logger)
    {
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the balancer and worker status of every configured Apache server.
    /// </summary>
    /// <remarks>
    /// All servers are queried concurrently. Unreachable servers do not fail the request:
    /// they are reported in the <c>Errors</c> array while reachable servers are returned
    /// in <c>SuccessData</c> (partial success, always HTTP 200).
    /// </remarks>
    [HttpGet("status")]
    [ProducesResponseType(typeof(MultiServerResponseDto<ServerStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<MultiServerResponseDto<ServerStatusDto>>> GetStatus(CancellationToken cancellationToken)
    {
        MultiServerResponseDto<ServerStatusDto> response = await _orchestrationService.GetClusterStatusAsync(cancellationToken);

        _logger.LogInformation(
            "Cluster status aggregated: {SuccessCount} server(s) succeeded, {ErrorCount} failed.",
            response.SuccessData.Count,
            response.Errors.Count);

        return Ok(response);
    }

    /// <summary>
    /// Changes the status of a single worker on a single Apache server.
    /// </summary>
    /// <remarks>
    /// The BFF first fetches the server's balancer-manager page to obtain a fresh per-balancer
    /// nonce, then submits the form-encoded modification POST that the native UI would send,
    /// and finally returns the worker's post-action state scraped from Apache's response.
    ///
    /// Valid <c>ActionType</c> values (case-insensitive): Enable, Disable, Drain, Stop,
    /// DisableOff, DrainOff, StopOff, IgnoreErrorsOn, IgnoreErrorsOff, HotStandbyOn,
    /// HotStandbyOff, HotSpareOn, HotSpareOff, HcFailOn, HcFailOff.
    /// </remarks>
    /// <response code="200">The action was applied; the body contains the worker's new status.</response>
    /// <response code="400">Invalid ServerId, BalancerName, WorkerUrl or ActionType.</response>
    /// <response code="502">The Apache server could not be reached or rejected the request.</response>
    [HttpPost("worker/status")]
    [ProducesResponseType(typeof(WorkerActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SetWorkerStatus([FromBody] WorkerActionRequestDto request, CancellationToken cancellationToken)
    {
        WorkerActionOutcome outcome = await _orchestrationService.ExecuteWorkerActionAsync(request, cancellationToken);

        if (!outcome.Succeeded)
        {
            _logger.LogWarning(
                "Worker action {Action} on server {ServerId} failed with {StatusCode}: {Reason}",
                request.ActionType,
                request.ServerId,
                outcome.FailureStatusCode,
                outcome.FailureReason);

            return Problem(
                detail: outcome.FailureReason,
                statusCode: outcome.FailureStatusCode ?? StatusCodes.Status502BadGateway,
                title: outcome.FailureStatusCode == StatusCodes.Status400BadRequest
                    ? "Invalid worker action request"
                    : "Upstream Apache server error");
        }

        return Ok(outcome.Result);
    }
}
