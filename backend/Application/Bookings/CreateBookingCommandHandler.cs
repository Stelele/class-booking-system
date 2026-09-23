using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Domain.Slots.Booking;

namespace Application.Bookings;

public sealed class CreateBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet, IBookingNotifier notifier)
    : ICommandHandler<CreateBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(CreateBookingCommand c, CancellationToken ct)
    {
        var blocked = await db.BlockedDays.Select(b => b.Date).ToListAsync(ct);
        var error = BookingPolicy.Validate(c.Date, DateTime.UtcNow, blocked);
        if (error is not null) throw new BookingException(error);

        var existingActive = await db.Bookings.AnyAsync(b =>
            b.StudentId == user.UserId && b.Status == BookingStatus.Active &&
            b.Slot.Date == c.Date, ct);
        if (existingActive) throw new BookingException("You already have a booking on that day.");

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct);
        if (slot is null)
        {
            slot = new Slot { Date = c.Date };
            db.Slots.Add(slot);
        }

        if (slot.MeetLink is null)
        {
            var link = await meet.GetOrCreateLinkAsync(c.Date, ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
        }

        var booking = new BookingEntity { SlotId = slot.Id, StudentId = user.UserId };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // concurrent first-booking on the same day: unique Slot.Date index lost the race —
            // re-attach to the winning slot and retry once; if the failure was a duplicate
            // active booking by this student, reject cleanly instead of retry-looping
            db.ChangeTracker.Clear();
            var winner = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct);
            if (winner is null) throw;
            var dupeActive = await db.Bookings.AnyAsync(b =>
                b.StudentId == user.UserId && b.Status == BookingStatus.Active && b.SlotId == winner.Id, ct);
            if (dupeActive) throw new BookingException("You already have a booking on that day.");
            booking = new BookingEntity { SlotId = winner.Id, StudentId = user.UserId };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            slot = winner;
        }

        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Created, ct);

        return new BookingDto(booking.Id, c.Date, LessonTime.StartUtc(c.Date), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: null, MeetLink: slot.MeetLink);
    }
}
