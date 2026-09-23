namespace Booking.Application.Abstractions;

public interface ITwilioSender
{
    Task<string> SendAsync(string toE164, string body, CancellationToken ct);
}
