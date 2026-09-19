using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class CancelBookingCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<CancelBookingCommand, bool>
{
    public async Task<bool> Handle(CancelBookingCommand c, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.Id == c.BookingId, ct)
            ?? throw new BookingException("Booking not found.");

        if (booking.StudentId != user.UserId && !user.IsAdmin)
            throw new BookingException("You can only cancel your own bookings.");

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
