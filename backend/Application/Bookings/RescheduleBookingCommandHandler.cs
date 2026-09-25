using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

public sealed class RescheduleBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet, IMeetEventSync sync, IBookingNotifier notifier)
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

        var oldSlotId = booking.SlotId;
        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct);
        if (slot is null)
        {
            slot = new Slot { Date = c.NewDate };
            db.Slots.Add(slot);
        }

        if (slot.MeetLink is null
            || !await BookingSlotLifecycle.HasActiveBookingsAsync(db, slot, ct))
        {
            var link = await meet.GetOrCreateLinkAsync(c.NewDate, ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
        }

        string? releasedGoogleEventId = null;
        if (oldSlotId != slot.Id)
        {
            releasedGoogleEventId = await BookingSlotLifecycle.ReleaseIfUnusedAsync(
                db, booking.Slot, booking.Id, ct);
        }

        var originalDate = booking.OriginalDate ?? booking.Slot.Date;
        booking.OriginalDate = originalDate;
        booking.SlotId = slot.Id;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // concurrent move to the same fresh day: unique Slot.Date index lost the race —
            // re-attach to the winning slot, restore the audit fields the failed save
            // never persisted, and retry once; duplicate active booking → clean rejection
            db.ChangeTracker.Clear();
            var winner = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct);
            if (winner is null) throw;
            var dupeActive = await db.Bookings.AnyAsync(b =>
                b.StudentId == booking.StudentId && b.Status == BookingStatus.Active &&
                b.SlotId == winner.Id && b.Id != booking.Id, ct);
            if (dupeActive) throw new BookingException("You already have a booking on the new day.");
            booking = await db.Bookings.FirstAsync(b => b.Id == booking.Id, ct);
            booking.OriginalDate = originalDate;
            booking.SlotId = winner.Id;
            booking.UpdatedAtUtc = DateTime.UtcNow;
            slot = winner;
            if (oldSlotId != slot.Id)
            {
                var oldSlot = await db.Slots.FirstAsync(s => s.Id == oldSlotId, ct);
                releasedGoogleEventId = await BookingSlotLifecycle.ReleaseIfUnusedAsync(
                    db, oldSlot, booking.Id, ct);
            }
            await db.SaveChangesAsync(ct);
        }

        if (releasedGoogleEventId is not null)
            await sync.DeleteEventAsync(releasedGoogleEventId, ct);
        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Rescheduled, ct);

        return new BookingDto(booking.Id, c.NewDate, LessonTime.StartUtc(c.NewDate), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: booking.OriginalDate, MeetLink: slot.MeetLink);
    }
}
