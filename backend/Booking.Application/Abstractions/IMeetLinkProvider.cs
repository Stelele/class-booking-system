namespace Booking.Application.Abstractions;

public interface IMeetLinkProvider
{
    Task<string> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default);
}
