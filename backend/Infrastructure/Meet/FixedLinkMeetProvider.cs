using Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Meet;

/// Slice 1: one recurring Meet link from config. Slice 2 swaps in GoogleCalendarMeetProvider.
public sealed class FixedLinkMeetProvider(IConfiguration config) : IMeetLinkProvider
{
    // attendeeEmails is ignored: fixed mode reuses one config link and creates
    // no calendar event, so there is no guest list to maintain.
    public Task<MeetLinkResult> GetOrCreateLinkAsync(
        DateOnly date, IReadOnlyList<string> attendeeEmails, CancellationToken ct = default)
        => Task.FromResult(new MeetLinkResult(
            config["App:FixedMeetLink"]
                ?? throw new InvalidOperationException("App:FixedMeetLink is not configured."),
            null));
}
