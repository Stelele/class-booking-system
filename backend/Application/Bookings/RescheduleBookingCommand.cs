using Application.Abstractions;
using Application.DTOs;

namespace Application.Bookings;

public sealed record RescheduleBookingCommand(Guid BookingId, DateOnly NewDate) : ICommand<BookingDto>;
