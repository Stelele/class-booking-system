using Application.Abstractions;

namespace Application.Bookings;

public sealed record CancelBookingCommand(Guid BookingId) : ICommand<bool>;
