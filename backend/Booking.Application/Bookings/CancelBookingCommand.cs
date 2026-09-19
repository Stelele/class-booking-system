using Booking.Application.Abstractions;

namespace Booking.Application.Bookings;

public sealed record CancelBookingCommand(Guid BookingId) : ICommand<bool>;
