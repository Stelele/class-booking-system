using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Meet;

public sealed class FixedLinkEventSync : IMeetEventSync
{
    public Task DeleteEventAsync(string googleEventId, CancellationToken ct) => Task.CompletedTask;
}
