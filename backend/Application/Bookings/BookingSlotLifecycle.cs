using Application.Abstractions;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

internal static class BookingSlotLifecycle
{
    public static Task<bool> HasActiveBookingsAsync(
        IAppDbContext db,
        Slot slot,
        CancellationToken ct) =>
        db.Bookings.AnyAsync(
            b => b.SlotId == slot.Id && b.Status == BookingStatus.Active,
            ct);

    /// <summary>
    /// Emails of every student who currently holds this day. Callers use it to
    /// rebuild the Calendar guest list from scratch, which is what keeps a
    /// cancelled student off the guest list — <see cref="UpdateAttendeesAsync"/>
    /// replaces the list rather than removing one entry, so a stale address
    /// would otherwise linger as an invitation.
    /// Must be called after the booking change is saved, or the newest
    /// booking is not yet visible to the query.
    /// </summary>
    public static async Task<List<string>> ActiveStudentEmailsAsync(
        IAppDbContext db,
        Slot slot,
        CancellationToken ct)
    {
        var studentIds = await db.Bookings.AsNoTracking()
            .Where(b => b.SlotId == slot.Id && b.Status == BookingStatus.Active)
            .Select(b => b.StudentId)
            .ToListAsync(ct);
        if (studentIds.Count == 0) return [];

        return await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id))
            .Select(u => u.Email)
            .ToListAsync(ct);
    }

    public static async Task<string?> ReleaseIfUnusedAsync(
        IAppDbContext db,
        Slot slot,
        Guid bookingId,
        CancellationToken ct)
    {
        var hasOtherActiveBooking = await db.Bookings.AnyAsync(
            b => b.SlotId == slot.Id
                && b.Id != bookingId
                && b.Status == BookingStatus.Active,
            ct);
        if (hasOtherActiveBooking) return null;

        var googleEventId = slot.GoogleEventId;
        slot.MeetLink = null;
        slot.GoogleEventId = null;
        return googleEventId;
    }
}
