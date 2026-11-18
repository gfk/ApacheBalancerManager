using System.Text.Json;
using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Domain;
using Microsoft.Extensions.Options;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Remembers which workers the watchdog disabled, on disk, so that the one thing it must never
/// do — put back a worker somebody else took out — stays impossible across restarts.
/// </summary>
/// <remarks>
/// A failure to persist is treated as a reason not to act rather than something to log and move
/// past: a disable the watchdog cannot remember is a backend nobody will re-enable.
/// </remarks>
public sealed class WatchdogStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<WatchdogStateStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<WatchdogDisabledWorker> _entries = new();

    public WatchdogStateStore(IOptions<WatchdogOptions> options, ILogger<WatchdogStateStore> logger)
    {
        string? configured = options.Value.StateFilePath;
        _path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "watchdog-state.json")
            : configured;

        _logger = logger;
    }

    /// <summary>Absolute path of the state file, for logging at startup.</summary>
    public string FilePath => _path;

    /// <summary>
    /// Reads the state file. A missing file means "nothing disabled" and is normal on a first run;
    /// an unreadable one is reported and treated the same way, because refusing to start would
    /// leave the cluster with no supervision at all.
    /// </summary>
    public async Task<IReadOnlyList<WatchdogDisabledWorker>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                _entries = new List<WatchdogDisabledWorker>();
                return _entries;
            }

            await using FileStream stream = File.OpenRead(_path);
            _entries = await JsonSerializer.DeserializeAsync<List<WatchdogDisabledWorker>>(stream, SerializerOptions, cancellationToken)
                       ?? new List<WatchdogDisabledWorker>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(
                exception,
                "Could not read the watchdog state file '{Path}'. Starting with an empty state: workers disabled before this restart will have to be re-enabled by hand.",
                _path);

            _entries = new List<WatchdogDisabledWorker>();
        }
        finally
        {
            _gate.Release();
        }

        return _entries;
    }

    /// <summary>Entries recorded for one target.</summary>
    public IReadOnlyList<WatchdogDisabledWorker> For(string targetId)
    {
        return _entries
            .Where((WatchdogDisabledWorker entry) => string.Equals(entry.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Records newly disabled workers. Returns false when they could not be persisted.</summary>
    public async Task<bool> AddAsync(IEnumerable<WatchdogDisabledWorker> entries, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _entries.AddRange(entries);
            return await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets every entry of one target, after it has been put back in rotation.</summary>
    public async Task<bool> RemoveAsync(string targetId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _entries.RemoveAll((WatchdogDisabledWorker entry) =>
                string.Equals(entry.TargetId, targetId, StringComparison.OrdinalIgnoreCase));

            return await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write beside the real file and move it into place, so a crash mid-write cannot
            // leave behind a truncated state file that the next start would read as "nothing
            // was ever disabled".
            string temporary = _path + ".tmp";
            await using (FileStream stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, _entries, SerializerOptions, cancellationToken);
            }

            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not write the watchdog state file '{Path}'.", _path);
            return false;
        }
    }
}
