namespace Booking.Application.Abstractions;

public interface IMeetEventSync
{
    Task DeleteEventAsync(string googleEventId, CancellationToken ct = default);
}
