using ApacheBalancerWasmInterface.Models;

namespace ApacheBalancerWasmInterface.Services;

/// <summary>
/// Browser-side history of the worker counters, kept only for the pools the user has expanded.
/// Neither the frontend nor the BFF stores anything between polls, so a pool's graphs start empty
/// the moment its row is expanded, fill up with the auto-refresh, and are dropped on collapse.
/// </summary>
public sealed class WorkerMetricsHistory
{
    /// <summary>
    /// Samples kept per worker. At the default 1s refresh this is a two-minute window; at 15s, half
    /// an hour. Bounding by sample count (rather than by time) keeps the redraw cost predictable.
    /// </summary>
    public const int MaxSamplesPerWorker = 120;

    /// <summary>Number of categorical colour slots; workers past this share a single "Other" line.</summary>
    public const int ColorSlotCount = 8;

    private readonly Dictionary<string, PoolHistory> poolHistories =
        new Dictionary<string, PoolHistory>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Appends one sample per worker for every tracked pool, and forgets the pools that are no longer tracked.</summary>
    public void Record(IEnumerable<BalancerPoolViewModel> pools, IReadOnlyCollection<string> trackedPoolNames)
    {
        HashSet<string> tracked = new HashSet<string>(trackedPoolNames, StringComparer.OrdinalIgnoreCase);

        foreach (string poolName in new List<string>(poolHistories.Keys))
        {
            if (!tracked.Contains(poolName))
            {
                poolHistories.Remove(poolName);
            }
        }

        // One timestamp for the whole pass so every worker's sample lines up on the x axis.
        DateTime timestamp = DateTime.Now;
        foreach (BalancerPoolViewModel pool in pools)
        {
            if (tracked.Contains(pool.BalancerName))
            {
                RecordPool(pool, timestamp);
            }
        }
    }

    /// <summary>Appends a sample for a single pool — used to seed the graphs the instant the row is expanded.</summary>
    public void RecordPool(BalancerPoolViewModel pool)
    {
        RecordPool(pool, DateTime.Now);
    }

    /// <summary>Drops everything recorded for a pool, so re-expanding it starts a fresh graph.</summary>
    public void Forget(string balancerName)
    {
        poolHistories.Remove(balancerName);
    }

    /// <summary>The tracked workers of a pool, ordered by colour slot; empty when the pool is not expanded.</summary>
    public IReadOnlyList<TrackedWorker> GetWorkers(string balancerName)
    {
        if (!poolHistories.TryGetValue(balancerName, out PoolHistory? history))
        {
            return Array.Empty<TrackedWorker>();
        }

        return history.Snapshot();
    }

    private void RecordPool(BalancerPoolViewModel pool, DateTime timestamp)
    {
        if (!poolHistories.TryGetValue(pool.BalancerName, out PoolHistory? history))
        {
            history = new PoolHistory();
            poolHistories[pool.BalancerName] = history;
        }

        history.Record(pool.Workers, timestamp);
    }

    /// <summary>Per-worker sample buffers of one pool, plus the colour slot each worker was given.</summary>
    private sealed class PoolHistory
    {
        private readonly Dictionary<string, WorkerTrack> tracks = new Dictionary<string, WorkerTrack>(StringComparer.Ordinal);

        public void Record(IReadOnlyList<WorkerViewModel> workers, DateTime timestamp)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (WorkerViewModel worker in workers)
            {
                string key = worker.ServerId + "|" + worker.Url;
                seen.Add(key);

                if (!tracks.TryGetValue(key, out WorkerTrack? track))
                {
                    track = new WorkerTrack(worker.ServerId, worker.Url, NextFreeColorSlot());
                    tracks[key] = track;
                }

                // The exact counters when the server publishes them, Apache's rounded cells otherwise.
                // Both are byte totals of the same thing, so a server that gains or loses its
                // balancer-bytes endpoint mid-history just changes the resolution of the line.
                bool preciseBytes = worker.HasPreciseBytes;

                track.Samples.Add(new WorkerMetricSample(
                    timestamp,
                    ApacheMetricParser.ParseCount(worker.Elected),
                    ApacheMetricParser.ParseCount(worker.Busy),
                    preciseBytes ? worker.ToBytes!.Value : ApacheMetricParser.ParseBytes(worker.To),
                    preciseBytes ? worker.FromBytes!.Value : ApacheMetricParser.ParseBytes(worker.From),
                    preciseBytes));

                while (track.Samples.Count > MaxSamplesPerWorker)
                {
                    track.Samples.RemoveAt(0);
                }
            }

            // A worker that vanished from the pool takes its history — and frees its colour — with it.
            foreach (string key in new List<string>(tracks.Keys))
            {
                if (!seen.Contains(key))
                {
                    tracks.Remove(key);
                }
            }
        }

        public IReadOnlyList<TrackedWorker> Snapshot()
        {
            List<TrackedWorker> snapshot = new List<TrackedWorker>(tracks.Count);
            foreach (KeyValuePair<string, WorkerTrack> entry in tracks)
            {
                snapshot.Add(new TrackedWorker
                {
                    Key = entry.Key,
                    ServerId = entry.Value.ServerId,
                    Url = entry.Value.Url,
                    ColorSlot = entry.Value.ColorSlot,
                    Samples = entry.Value.Samples
                });
            }

            snapshot.Sort((TrackedWorker left, TrackedWorker right) =>
            {
                int slotComparison = left.ColorSlot.CompareTo(right.ColorSlot);
                return slotComparison != 0 ? slotComparison : string.CompareOrdinal(left.Key, right.Key);
            });

            return snapshot;
        }

        /// <summary>Smallest slot no live worker holds, so colours stay stable as workers are added and removed.</summary>
        private int NextFreeColorSlot()
        {
            HashSet<int> taken = new HashSet<int>();
            foreach (WorkerTrack track in tracks.Values)
            {
                taken.Add(track.ColorSlot);
            }

            int slot = 0;
            while (taken.Contains(slot))
            {
                slot++;
            }
            return slot;
        }
    }

    private sealed class WorkerTrack
    {
        public WorkerTrack(string serverId, string url, int colorSlot)
        {
            ServerId = serverId;
            Url = url;
            ColorSlot = colorSlot;
        }

        public string ServerId { get; }

        public string Url { get; }

        public int ColorSlot { get; }

        public List<WorkerMetricSample> Samples { get; } = new List<WorkerMetricSample>();
    }
}
