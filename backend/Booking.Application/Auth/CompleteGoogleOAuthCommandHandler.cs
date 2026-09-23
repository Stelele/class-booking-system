using Booking.Application.Abstractions;
using Booking.Application.Bookings;

namespace Booking.Application.Auth;

public sealed class CompleteGoogleOAuthCommandHandler(
    IGoogleOAuthStateStore states, IGoogleAccountConnector connector, ICurrentUser user)
    : ICommandHandler<CompleteGoogleOAuthCommand, bool>
{
    public async Task<bool> Handle(CompleteGoogleOAuthCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        if (!states.Consume(c.State)) throw new BookingException("OAuth state mismatch — restart the connect flow.");
        await connector.ConnectAsync(c.Code, user.UserId, ct);
        return true;
    }
}
