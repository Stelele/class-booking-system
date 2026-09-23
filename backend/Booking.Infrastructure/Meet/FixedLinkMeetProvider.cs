using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Booking.Infrastructure.Meet;

/// Slice 1: one recurring Meet link from config. Slice 2 swaps in GoogleCalendarMeetProvider.
public sealed class FixedLinkMeetProvider(IConfiguration config) : IMeetLinkProvider
{
    public Task<MeetLinkResult> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default)
        => Task.FromResult(new MeetLinkResult(
            config["App:FixedMeetLink"]
                ?? throw new InvalidOperationException("App:FixedMeetLink is not configured."),
            null));
}
