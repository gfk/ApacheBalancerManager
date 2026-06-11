namespace Shared.Dtos;

/// <summary>Error payload for a single Apache server that could not be queried.</summary>
public sealed class ServerErrorDto
{
    /// <summary>Configured identifier of the failing server (e.g. "lb-02").</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Human-readable description of the failure (e.g. "Connection timed out").</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Moment the failure was observed (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }
}
