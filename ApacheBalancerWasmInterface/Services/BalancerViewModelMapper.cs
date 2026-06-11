using ApacheBalancerWasmInterface.Models;
using Shared.Dtos;

namespace ApacheBalancerWasmInterface.Services;

/// <summary>
/// Transforms the BFF's server-first hierarchy (Server -> Balancers -> Workers) into the
/// pool-first hierarchy the dashboard displays (Pool -> Workers stamped with their ServerId).
/// </summary>
public static class BalancerViewModelMapper
{
    public static List<BalancerPoolViewModel> ToPoolFirst(MultiServerResponseDto<ServerStatusDto> response)
    {
        Dictionary<string, List<WorkerViewModel>> workersByPool = new Dictionary<string, List<WorkerViewModel>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> methodByPool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ServerStatusDto server in response.SuccessData)
        {
            foreach (BalancerStatusDto balancer in server.Balancers)
            {
                if (!workersByPool.TryGetValue(balancer.Name, out List<WorkerViewModel>? poolWorkers))
                {
                    poolWorkers = new List<WorkerViewModel>();
                    workersByPool[balancer.Name] = poolWorkers;
                    methodByPool[balancer.Name] = balancer.Method;
                }

                foreach (WorkerStatusDto worker in balancer.Workers)
                {
                    poolWorkers.Add(new WorkerViewModel
                    {
                        ServerId = server.ServerId,
                        BalancerName = balancer.Name,
                        Worker = worker
                    });
                }
            }
        }

        List<BalancerPoolViewModel> pools = new List<BalancerPoolViewModel>();
        foreach (KeyValuePair<string, List<WorkerViewModel>> entry in workersByPool)
        {
            // Sort by URL first so the same worker on lb-01/lb-02 lands on adjacent rows.
            entry.Value.Sort((WorkerViewModel left, WorkerViewModel right) =>
            {
                int urlComparison = string.Compare(left.Url, right.Url, StringComparison.OrdinalIgnoreCase);
                if (urlComparison != 0)
                {
                    return urlComparison;
                }
                return string.Compare(left.ServerId, right.ServerId, StringComparison.OrdinalIgnoreCase);
            });

            pools.Add(new BalancerPoolViewModel
            {
                BalancerName = entry.Key,
                Method = methodByPool[entry.Key],
                Workers = entry.Value
            });
        }

        pools.Sort((BalancerPoolViewModel left, BalancerPoolViewModel right) =>
            string.Compare(left.BalancerName, right.BalancerName, StringComparison.OrdinalIgnoreCase));

        return pools;
    }
}
