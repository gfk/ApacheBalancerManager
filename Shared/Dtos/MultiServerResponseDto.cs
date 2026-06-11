namespace Shared.Dtos;

/// <summary>
/// Top-level partial-success wrapper. Successfully scraped servers land in
/// <see cref="SuccessData"/>; unreachable or failed servers land in <see cref="Errors"/>.
/// The HTTP status is 200 even when some servers failed.
/// </summary>
/// <typeparam name="T">Per-server payload type.</typeparam>
public sealed class MultiServerResponseDto<T>
{
    /// <summary>Successfully scraped data, one entry per reachable server.</summary>
    public List<T> SuccessData { get; set; } = new();

    /// <summary>Detailed error payloads for the servers that could not be queried.</summary>
    public List<ServerErrorDto> Errors { get; set; } = new();
}
