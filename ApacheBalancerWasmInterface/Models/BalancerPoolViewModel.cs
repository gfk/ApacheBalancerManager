namespace ApacheBalancerWasmInterface.Models;

/// <summary>
/// Pool-first root: one balancer pool aggregated across every Apache server,
/// holding the flattened list of its workers on all servers.
/// </summary>
public sealed class BalancerPoolViewModel
{
    /// <summary>Pool name (e.g. "web-public-backend").</summary>
    public string BalancerName { get; init; } = string.Empty;

    /// <summary>Load-balancing method of the pool (e.g. "byrequests", "bybusyness").</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>All workers of this pool across every server, sorted by URL then server.</summary>
    public List<WorkerViewModel> Workers { get; init; } = new List<WorkerViewModel>();

    public int WorkerCount => Workers.Count;

    /// <summary>Number of distinct Apache servers exposing this pool.</summary>
    public int ServerCount
    {
        get
        {
            HashSet<string> serverIds = new HashSet<string>();
            foreach (WorkerViewModel worker in Workers)
            {
                serverIds.Add(worker.ServerId);
            }
            return serverIds.Count;
        }
    }

    public int OkCount
    {
        get
        {
            int count = 0;
            foreach (WorkerViewModel worker in Workers)
            {
                if (worker.StatusFlags.Contains("Ok"))
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int StandbyCount
    {
        get
        {
            int count = 0;
            foreach (WorkerViewModel worker in Workers)
            {
                if (worker.IsStandby)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int DisabledCount
    {
        get
        {
            int count = 0;
            foreach (WorkerViewModel worker in Workers)
            {
                if (worker.IsDisabled || worker.IsDraining)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>True when any worker of the pool is in an error-like state.</summary>
    public bool HasError
    {
        get
        {
            foreach (WorkerViewModel worker in Workers)
            {
                if (worker.HasError)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
