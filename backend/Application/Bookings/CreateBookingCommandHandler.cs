using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Domain.Slots.Booking;

namespace Application.Bookings;

public sealed class CreateBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet, IMeetEventSync sync, IBookingNotifier notifier)
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

        var studentEmail = await db.Users.AsNoTracking()
            .Where(u => u.Id == user.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct)
            ?? throw new BookingException("Your account has no email address.");

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct);
        if (slot is null)
        {
            slot = new Slot { Date = c.Date };
            db.Slots.Add(slot);
        }

        // Tracks whether the event above was created for the slot this booking
        // ends up on. If it already existed, this student joined an existing
        // event and must be added to its guest list separately — the insert
        // would not have run for them.
        var insertedEventForSlotId = (Guid?)null;
        if (slot.MeetLink is null
            || !await BookingSlotLifecycle.HasActiveBookingsAsync(db, slot, ct))
        {
            // The event is created with this student as its only guest. Their
            // email is looked up here rather than on ICurrentUser because the
            // guest list needs the address, and the claims identity carries only
            // the id and name.
            var link = await meet.GetOrCreateLinkAsync(c.Date, [studentEmail], ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
            insertedEventForSlotId = slot.Id;
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

        // A combined lesson reuses the existing event, so the insert above never
        // ran for this student. Patch the guest list after the save so the query
        // sees the new booking. Skipped when the insert ran for the slot we ended
        // up on — that event already lists this student, as its only guest.
        // Losing the concurrent-booking retry counts as "not inserted", since
        // the winning slot's event belongs to the other student. No-op in fixed
        // mode (no event id).
        if (insertedEventForSlotId != slot.Id)
        {
            await sync.UpdateAttendeesAsync(
                slot.GoogleEventId,
                await BookingSlotLifecycle.ActiveStudentEmailsAsync(db, slot, ct),
                ct);
        }

        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Created, ct);

        return new BookingDto(booking.Id, c.Date, LessonTime.StartUtc(c.Date), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: null, MeetLink: slot.MeetLink);
    }
}
