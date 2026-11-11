using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Domain;
using Microsoft.Extensions.Options;
using Shared.Dtos;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// The external watchdog. Probes every configured backend on its own schedule and, when one stops
/// serving, disables the matching worker on every Apache server through /balancer-manager.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately conservative. An automaton that can take production backends out of
/// rotation is a new way to cause an outage, so every path that leads to an action is guarded:
/// it will not act while its peer backend is also failing, nor if that would leave a pool with no
/// usable worker, nor if any Apache server could not be read, nor beyond a fixed number of
/// actions per hour. When only some servers accept the change it rolls the others back, because
/// a half-applied change is harder to reason about than no change at all.
/// </para>
/// <para>
/// It also never re-enables a worker it did not disable itself; see
/// <see cref="WatchdogStateStore"/>.
/// </para>
/// </remarks>
public sealed class WatchdogService : BackgroundService
{
    private static readonly string[] BlockingStatusFlags = { "Dis", "Stop", "Err" };
    private static readonly TimeSpan RefusalLogInterval = TimeSpan.FromMinutes(5);

    private readonly WatchdogOptions _options;
    private readonly ApacheManagementOptions _apacheOptions;
    private readonly IBackendProbe _probe;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WatchdogStateStore _stateStore;
    private readonly ILogger<WatchdogService> _logger;

    private readonly Dictionary<string, TargetRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> _recentActions = new();

    public WatchdogService(
        IOptions<WatchdogOptions> options,
        IOptions<ApacheManagementOptions> apacheOptions,
        IBackendProbe probe,
        IServiceScopeFactory scopeFactory,
        WatchdogStateStore stateStore,
        ILogger<WatchdogService> logger)
    {
        _options = options.Value;
        _apacheOptions = apacheOptions.Value;
        _probe = probe;
        _scopeFactory = scopeFactory;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartUpAsync(stoppingToken))
        {
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)));
        try
        {
            do
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One bad cycle must not end the supervision.
                    _logger.LogError(exception, "Watchdog cycle failed; the loop continues.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Watchdog stopping.");
        }
    }

    /// <summary>Validates the configuration and restores what the previous run had disabled.</summary>
    /// <returns>False when the watchdog has nothing to do and the loop should not start.</returns>
    private async Task<bool> StartUpAsync(CancellationToken cancellationToken)
    {
        // A backstop rather than the real switch: Program.cs does not register this service at all
        // unless the section is enabled. It matters only if something hosts the class directly.
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Watchdog is off ('{Section}:Enabled' is false). Apache's own health checks are the only supervision.",
                WatchdogOptions.SectionName);

            return false;
        }

        if (_options.Targets.Count == 0)
        {
            _logger.LogWarning(
                "Watchdog is on but '{Section}:Targets' is empty; nothing to probe.",
                WatchdogOptions.SectionName);

            return false;
        }

        if (_apacheOptions.Servers.Count == 0)
        {
            _logger.LogError(
                "Watchdog is on but no Apache server is configured under '{Section}:Servers'; it would have nowhere to act.",
                ApacheManagementOptions.SectionName);

            return false;
        }

        foreach (WatchdogTargetOptions target in _options.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Id))
            {
                _logger.LogError("A target has no Id; the watchdog will not start. Give every entry of '{Section}:Targets' a unique Id.", WatchdogOptions.SectionName);
                return false;
            }

            if (!_states.TryAdd(target.Id, new TargetRuntimeState()))
            {
                _logger.LogError("Target Id '{TargetId}' is used more than once; the watchdog will not start.", target.Id);
                return false;
            }
        }

        foreach (WatchdogTargetOptions target in _options.Targets)
        {
            if (target.PeerTargetId is { Length: > 0 } peerId && !_states.ContainsKey(peerId))
            {
                _logger.LogError(
                    "Target {TargetId} names peer '{PeerId}', which is not a configured target. It will never be disabled until this is fixed.",
                    target.Id,
                    peerId);
            }
        }

        IReadOnlyList<WatchdogDisabledWorker> carriedOver = await _stateStore.LoadAsync(cancellationToken);
        foreach (WatchdogDisabledWorker entry in carriedOver)
        {
            if (_states.TryGetValue(entry.TargetId, out TargetRuntimeState? state))
            {
                state.DisabledByWatchdog = true;
            }
            else
            {
                _logger.LogWarning(
                    "The state file lists worker {Worker} on server {ServerId} as disabled for target '{TargetId}', which is no longer configured. Re-enable it by hand if it should be serving.",
                    entry.WorkerUrl,
                    entry.ServerId,
                    entry.TargetId);
            }
        }

        _logger.LogInformation(
            "Watchdog starting in {Mode}: {TargetCount} target(s) across {ServerCount} Apache server(s), probing every {Interval}s. " +
            "Disable after {Failures} consecutive failures, re-enable after {Successes} consecutive successes, at most {MaxActions} action(s) per hour. " +
            "State file: {StateFile}. Carried over {CarriedOver} disabled worker(s).",
            _options.DryRun ? "OBSERVE-ONLY mode — decisions are logged, nothing is sent to Apache" : "ARMED mode",
            _options.Targets.Count,
            _apacheOptions.Servers.Count,
            _options.PollIntervalSeconds,
            _options.FailuresBeforeDisable,
            _options.SuccessesBeforeEnable,
            _options.MaxActionsPerHour,
            _stateStore.FilePath,
            carriedOver.Count);

        await ReportUnknownTargetsAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Checks once, at startup, that every configured balancer and worker actually exists on the
    /// proxies. A typo found here is a warning in a quiet log; found during an incident it is a
    /// failed action at the worst possible moment.
    /// </summary>
    private async Task ReportUnknownTargetsAsync(CancellationToken cancellationToken)
    {
        MultiServerResponseDto<ServerStatusDto> cluster;
        try
        {
            cluster = await GetClusterStatusAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not read the cluster at startup; skipping target validation.");
            return;
        }

        foreach (ServerStatusDto server in cluster.SuccessData)
        {
            foreach (WatchdogTargetOptions target in _options.Targets)
            {
                BalancerStatusDto? balancer = FindBalancer(server, target.BalancerName);
                if (balancer is null)
                {
                    _logger.LogWarning(
                        "Target {TargetId}: server {ServerId} has no balancer named '{Balancer}'.",
                        target.Id,
                        server.ServerId,
                        target.BalancerName);

                    continue;
                }

                if (FindWorker(balancer, target.WorkerUrl) is null)
                {
                    _logger.LogWarning(
                        "Target {TargetId}: balancer '{Balancer}' on server {ServerId} has no worker '{Worker}'. Known workers: {Known}.",
                        target.Id,
                        target.BalancerName,
                        server.ServerId,
                        target.WorkerUrl,
                        string.Join(", ", balancer.Workers.Select((WorkerStatusDto worker) => worker.Url)));
                }
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        ProbeOutcome[] outcomes = await Task.WhenAll(
            _options.Targets.Select((WatchdogTargetOptions target) => _probe.ProbeAsync(target, cancellationToken)));

        Dictionary<string, ProbeOutcome> byTargetId = outcomes.ToDictionary(
            (ProbeOutcome outcome) => outcome.TargetId,
            StringComparer.OrdinalIgnoreCase);

        // Read once per cycle, and only when a decision actually needs it: scraping every
        // balancer-manager page every ten seconds would be a lot of load for nothing.
        MultiServerResponseDto<ServerStatusDto>? cluster = null;

        foreach (WatchdogTargetOptions target in _options.Targets)
        {
            ProbeOutcome outcome = byTargetId[target.Id];
            TargetRuntimeState state = _states[target.Id];
            state.Record(outcome);

            if (!outcome.Healthy)
            {
                _logger.LogWarning(
                    "Probe {TargetId} failed ({Streak} in a row, {Elapsed} ms): {Failure}",
                    target.Id,
                    state.ConsecutiveFailures,
                    outcome.ElapsedMilliseconds,
                    outcome.Failure);
            }
            else if (state.ConsecutiveSuccesses == 1 && state.DisabledByWatchdog)
            {
                _logger.LogInformation(
                    "Probe {TargetId} is answering again ({Elapsed} ms); it needs {Successes} consecutive successes before coming back.",
                    target.Id,
                    outcome.ElapsedMilliseconds,
                    _options.SuccessesBeforeEnable);
            }

            bool shouldDisable = !state.DisabledByWatchdog && state.ConsecutiveFailures >= _options.FailuresBeforeDisable;
            bool shouldEnable = state.DisabledByWatchdog && state.ConsecutiveSuccesses >= _options.SuccessesBeforeEnable;

            if (!shouldDisable && !shouldEnable)
            {
                continue;
            }

            cluster ??= await GetClusterStatusAsync(cancellationToken);

            if (shouldDisable)
            {
                await TryDisableAsync(target, state, byTargetId, cluster, cancellationToken);
            }
            else
            {
                await TryEnableAsync(target, state, cancellationToken);
            }
        }
    }

    private async Task TryDisableAsync(
        WatchdogTargetOptions target,
        TargetRuntimeState state,
        IReadOnlyDictionary<string, ProbeOutcome> byTargetId,
        MultiServerResponseDto<ServerStatusDto> cluster,
        CancellationToken cancellationToken)
    {
        string? refusal = EvaluateDisableGuards(target, byTargetId, cluster);
        if (refusal is not null)
        {
            LogRefusal(target, state, refusal);
            return;
        }

        state.ClearRefusal();
        string reason = state.Last?.Failure ?? "unhealthy";

        if (_options.DryRun)
        {
            _logger.LogWarning(
                "OBSERVE-ONLY: would disable worker {Worker} in balancer '{Balancer}' on {ServerCount} Apache server(s) after {Streak} failed probes — {Reason}",
                target.WorkerUrl,
                target.BalancerName,
                _apacheOptions.Servers.Count,
                state.ConsecutiveFailures,
                reason);

            state.DisabledByWatchdog = true;
            return;
        }

        List<WatchdogDisabledWorker> applied = new();
        List<string> failures = new();

        foreach (ApacheServerOptions server in _apacheOptions.Servers)
        {
            WorkerActionOutcome result = await ApplyAsync(server.Id, target, WorkerActionType.Disable, cancellationToken);
            if (result.Succeeded)
            {
                applied.Add(new WatchdogDisabledWorker
                {
                    TargetId = target.Id,
                    ServerId = server.Id,
                    BalancerName = target.BalancerName,
                    WorkerUrl = target.WorkerUrl,
                    DisabledAt = DateTimeOffset.UtcNow,
                    Reason = reason
                });
            }
            else
            {
                failures.Add($"{server.Id}: {result.FailureReason}");
            }
        }

        if (failures.Count > 0)
        {
            _logger.LogError(
                "Disabling worker {Worker} succeeded on {AppliedCount} server(s) but failed on {FailedCount} ({Failures}). Rolling back so the cluster stays consistent; the next cycle will try again.",
                target.WorkerUrl,
                applied.Count,
                failures.Count,
                string.Join(" | ", failures));

            await RollBackAsync(target, applied, cancellationToken);
            return;
        }

        RecordAction();
        state.DisabledByWatchdog = true;

        if (!await _stateStore.AddAsync(applied, cancellationToken))
        {
            _logger.LogError(
                "Worker {Worker} was disabled on every server but the state file could not be written. It will NOT be re-enabled automatically — put it back by hand once the backend recovers.",
                target.WorkerUrl);
        }

        _logger.LogError(
            "Disabled worker {Worker} in balancer '{Balancer}' on all {ServerCount} Apache server(s) after {Streak} failed probes — {Reason}",
            target.WorkerUrl,
            target.BalancerName,
            _apacheOptions.Servers.Count,
            state.ConsecutiveFailures,
            reason);
    }

    private async Task TryEnableAsync(WatchdogTargetOptions target, TargetRuntimeState state, CancellationToken cancellationToken)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation(
                "OBSERVE-ONLY: would re-enable worker {Worker} in balancer '{Balancer}' after {Streak} consecutive successful probes.",
                target.WorkerUrl,
                target.BalancerName,
                state.ConsecutiveSuccesses);

            state.DisabledByWatchdog = false;
            return;
        }

        IReadOnlyList<WatchdogDisabledWorker> owned = _stateStore.For(target.Id);
        if (owned.Count == 0)
        {
            _logger.LogWarning(
                "Target {TargetId} is marked as disabled by the watchdog but the state file holds no entry for it; leaving it alone.",
                target.Id);

            state.DisabledByWatchdog = false;
            return;
        }

        List<string> failures = new();
        foreach (WatchdogDisabledWorker entry in owned)
        {
            // DisableOff rather than Enable: it clears only the flag the watchdog set. Enable
            // would also clear Drain and Stop, which may well have been set by a human.
            WorkerActionOutcome result = await ApplyAsync(entry.ServerId, target, WorkerActionType.DisableOff, cancellationToken);
            if (!result.Succeeded)
            {
                failures.Add($"{entry.ServerId}: {result.FailureReason}");
            }
        }

        if (failures.Count > 0)
        {
            _logger.LogError(
                "Could not re-enable worker {Worker} on {FailedCount} server(s) ({Failures}); will retry next cycle.",
                target.WorkerUrl,
                failures.Count,
                string.Join(" | ", failures));

            return;
        }

        RecordAction();
        state.DisabledByWatchdog = false;
        await _stateStore.RemoveAsync(target.Id, cancellationToken);

        _logger.LogWarning(
            "Re-enabled worker {Worker} in balancer '{Balancer}' after {Streak} consecutive successful probes.",
            target.WorkerUrl,
            target.BalancerName,
            state.ConsecutiveSuccesses);
    }

    /// <summary>
    /// Returns the reason the watchdog must not disable this worker, or null when it may.
    /// </summary>
    private string? EvaluateDisableGuards(
        WatchdogTargetOptions target,
        IReadOnlyDictionary<string, ProbeOutcome> byTargetId,
        MultiServerResponseDto<ServerStatusDto> cluster)
    {
        if (!HasActionBudget())
        {
            return $"the limit of {_options.MaxActionsPerHour} action(s) per hour is reached; a human should look at this before more backends are pulled out";
        }

        if (target.PeerTargetId is { Length: > 0 } peerId)
        {
            if (!byTargetId.TryGetValue(peerId, out ProbeOutcome? peer))
            {
                return $"peer target '{peerId}' is not configured, so there is no way to tell whether the other backend is healthy";
            }

            if (!peer.Healthy)
            {
                return $"peer target '{peerId}' is failing too ({peer.Failure}); the fault is most likely in something both backends share, and moving traffic between them would only cut the requests in flight";
            }
        }

        if (cluster.Errors.Count > 0)
        {
            string unreachable = string.Join(", ", cluster.Errors.Select((ServerErrorDto error) => error.ServerId));
            return $"server(s) {unreachable} could not be read, so the change could not be applied everywhere at once";
        }

        foreach (ServerStatusDto server in cluster.SuccessData)
        {
            BalancerStatusDto? balancer = FindBalancer(server, target.BalancerName);
            if (balancer is null)
            {
                continue;
            }

            bool anyoneLeft = balancer.Workers.Any((WorkerStatusDto worker) =>
                !IsSameWorker(worker.Url, target.WorkerUrl) && IsEligible(worker));

            if (!anyoneLeft)
            {
                return $"it is the last usable worker of balancer '{target.BalancerName}' on server {server.ServerId}; disabling it would take the site down rather than move it";
            }
        }

        return null;
    }

    private async Task RollBackAsync(WatchdogTargetOptions target, IEnumerable<WatchdogDisabledWorker> applied, CancellationToken cancellationToken)
    {
        foreach (WatchdogDisabledWorker entry in applied)
        {
            WorkerActionOutcome result = await ApplyAsync(entry.ServerId, target, WorkerActionType.DisableOff, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogCritical(
                    "Roll-back failed: worker {Worker} is still disabled on server {ServerId} ({Reason}). The cluster is in a mixed state and needs a human.",
                    entry.WorkerUrl,
                    entry.ServerId,
                    result.FailureReason);
            }
        }
    }

    private async Task<WorkerActionOutcome> ApplyAsync(
        string serverId,
        WatchdogTargetOptions target,
        WorkerActionType actionType,
        CancellationToken cancellationToken)
    {
        WorkerActionRequestDto request = new()
        {
            ServerId = serverId,
            BalancerName = target.BalancerName,
            WorkerUrl = target.WorkerUrl,
            ActionType = actionType.ToString()
        };

        using IServiceScope scope = _scopeFactory.CreateScope();
        IBalancerOrchestrationService orchestration = scope.ServiceProvider.GetRequiredService<IBalancerOrchestrationService>();

        try
        {
            return await orchestration.ExecuteWorkerActionAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected failure applying {Action} on server {ServerId}.", actionType, serverId);
            return WorkerActionOutcome.Failure(StatusCodes.Status500InternalServerError, exception.Message);
        }
    }

    private async Task<MultiServerResponseDto<ServerStatusDto>> GetClusterStatusAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IBalancerOrchestrationService orchestration = scope.ServiceProvider.GetRequiredService<IBalancerOrchestrationService>();
        return await orchestration.GetClusterStatusAsync(cancellationToken);
    }

    private bool HasActionBudget()
    {
        PruneActions();
        return _recentActions.Count < _options.MaxActionsPerHour;
    }

    private void RecordAction()
    {
        PruneActions();
        _recentActions.Enqueue(DateTimeOffset.UtcNow);
    }

    private void PruneActions()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        while (_recentActions.Count > 0 && _recentActions.Peek() < cutoff)
        {
            _recentActions.Dequeue();
        }
    }

    /// <summary>
    /// Logs why an action was refused, but not on every cycle: a backend that stays down with an
    /// equally sick peer would otherwise bury the log under the same line every ten seconds.
    /// </summary>
    private void LogRefusal(WatchdogTargetOptions target, TargetRuntimeState state, string refusal)
    {
        bool isNew = !string.Equals(state.LastRefusal, refusal, StringComparison.Ordinal);
        bool isStale = DateTimeOffset.UtcNow - state.LastRefusalLoggedAt > RefusalLogInterval;

        if (!isNew && !isStale)
        {
            return;
        }

        state.LastRefusal = refusal;
        state.LastRefusalLoggedAt = DateTimeOffset.UtcNow;

        _logger.LogWarning(
            "Target {TargetId} has failed {Streak} probes in a row but the watchdog is NOT disabling it: {Refusal}.",
            target.Id,
            state.ConsecutiveFailures,
            refusal);
    }

    private static BalancerStatusDto? FindBalancer(ServerStatusDto server, string balancerName)
    {
        return server.Balancers.FirstOrDefault((BalancerStatusDto balancer) =>
            string.Equals(balancer.Name, balancerName, StringComparison.OrdinalIgnoreCase));
    }

    private static WorkerStatusDto? FindWorker(BalancerStatusDto balancer, string workerUrl)
    {
        return balancer.Workers.FirstOrDefault((WorkerStatusDto worker) => IsSameWorker(worker.Url, workerUrl));
    }

    private static bool IsSameWorker(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a worker can still take traffic. A hot standby ("Stby") counts: it is idle by
    /// design and is exactly who should pick up the load.
    /// </summary>
    private static bool IsEligible(WorkerStatusDto worker)
    {
        if (!worker.StatusFlags.Contains("Ok", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !worker.StatusFlags.Any((string flag) => BlockingStatusFlags.Contains(flag, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>In-memory streak counters for one target; what must survive a restart lives in the state file.</summary>
    private sealed class TargetRuntimeState
    {
        public int ConsecutiveFailures { get; private set; }

        public int ConsecutiveSuccesses { get; private set; }

        public ProbeOutcome? Last { get; private set; }

        public bool DisabledByWatchdog { get; set; }

        public string? LastRefusal { get; set; }

        public DateTimeOffset LastRefusalLoggedAt { get; set; } = DateTimeOffset.MinValue;

        public void Record(ProbeOutcome outcome)
        {
            Last = outcome;

            if (outcome.Healthy)
            {
                ConsecutiveSuccesses++;
                ConsecutiveFailures = 0;
            }
            else
            {
                ConsecutiveFailures++;
                ConsecutiveSuccesses = 0;
            }
        }

        public void ClearRefusal()
        {
            LastRefusal = null;
            LastRefusalLoggedAt = DateTimeOffset.MinValue;
        }
    }
}
