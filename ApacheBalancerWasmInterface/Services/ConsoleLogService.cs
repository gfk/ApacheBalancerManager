namespace ApacheBalancerWasmInterface.Services;

/// <summary>Severity of a console log line, used to pick its colour.</summary>
public enum ConsoleSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>A single line shown in the on-screen console.</summary>
public sealed record ConsoleLogEntry(DateTime Timestamp, ConsoleSeverity Severity, string Message);

/// <summary>
/// Collects notification lines for the on-screen console window. Keeps a bounded
/// scrollback buffer and raises <see cref="OnChange"/> whenever a line is added.
/// </summary>
public sealed class ConsoleLogService
{
    private const int Capacity = 100;
    private readonly LinkedList<ConsoleLogEntry> entries = new LinkedList<ConsoleLogEntry>();

    /// <summary>Raised after a new line is appended so subscribers can re-render.</summary>
    public event Action? OnChange;

    /// <summary>The current scrollback buffer, oldest first.</summary>
    public IReadOnlyCollection<ConsoleLogEntry> Entries => entries;

    public void Write(ConsoleSeverity severity, string message)
    {
        entries.AddLast(new ConsoleLogEntry(DateTime.Now, severity, message));
        while (entries.Count > Capacity)
        {
            entries.RemoveFirst();
        }

        OnChange?.Invoke();
    }

    public void Info(string message) => Write(ConsoleSeverity.Info, message);

    public void Success(string message) => Write(ConsoleSeverity.Success, message);

    public void Warning(string message) => Write(ConsoleSeverity.Warning, message);

    public void Error(string message) => Write(ConsoleSeverity.Error, message);
}
