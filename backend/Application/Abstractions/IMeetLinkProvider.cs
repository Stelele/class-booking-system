namespace Application.Abstractions;

public sealed record MeetLinkResult(string MeetLink, string? GoogleEventId);

public interface IMeetLinkProvider
{
    /// <param name="attendeeEmails">
    /// Students to invite as guests on the new event. Omitted entirely when
    /// empty — an empty guest list would tell the calendar to invite nobody.
    /// </param>
    Task<MeetLinkResult> GetOrCreateLinkAsync(
        DateOnly date, IReadOnlyList<string> attendeeEmails, CancellationToken ct = default);
}
