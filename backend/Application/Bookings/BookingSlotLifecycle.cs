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
