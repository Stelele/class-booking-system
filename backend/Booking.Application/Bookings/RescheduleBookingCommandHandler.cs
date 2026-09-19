using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class RescheduleBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet)
    : ICommandHandler<RescheduleBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(RescheduleBookingCommand c, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.Id == c.BookingId, ct)
            ?? throw new BookingException("Booking not found.");

        if (booking.StudentId != user.UserId && !user.IsAdmin)
            throw new BookingException("You can only reschedule your own bookings.");

        var blocked = await db.BlockedDays.Select(b => b.Date).ToListAsync(ct);
        var error = BookingPolicy.Validate(c.NewDate, DateTime.UtcNow, blocked);
        if (error is not null) throw new BookingException(error);

        var clash = await db.Bookings.AnyAsync(b =>
            b.StudentId == booking.StudentId && b.Id != booking.Id &&
            b.Status == BookingStatus.Active && b.Slot.Date == c.NewDate, ct);
        if (clash) throw new BookingException("You already have a booking on the new day.");

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct)
                   ?? new Slot { Date = c.NewDate };
        if (slot.Id == Guid.Empty) db.Slots.Add(slot);

        slot.MeetLink ??= await meet.GetOrCreateLinkAsync(c.NewDate, ct);

        booking.OriginalDate ??= booking.Slot.Date;
        booking.SlotId = slot.Id;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new BookingDto(booking.Id, c.NewDate, LessonTime.StartUtc(c.NewDate), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: booking.OriginalDate, MeetLink: slot.MeetLink);
    }
}
