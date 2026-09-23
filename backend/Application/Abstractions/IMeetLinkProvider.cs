namespace Application.Abstractions;

public sealed record MeetLinkResult(string MeetLink, string? GoogleEventId);

public interface IMeetLinkProvider
{
    Task<MeetLinkResult> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default);
}
