namespace Booking.Application.Abstractions;

public interface ICodeSender
{
    Task SendAsync(string email, string name, string code, CancellationToken ct = default);
}
