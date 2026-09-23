namespace Booking.Application.Abstractions;

public enum BookingChangeKind { Created, Cancelled, Rescheduled }

public interface IBookingNotifier
{
    Task NotifyBookingChangedAsync(Guid bookingId, BookingChangeKind kind, CancellationToken ct);
}
