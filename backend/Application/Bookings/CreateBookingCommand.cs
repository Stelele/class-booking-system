using Application.Abstractions;
using Application.DTOs;

namespace Application.Bookings;

public sealed record CreateBookingCommand(DateOnly Date) : ICommand<BookingDto>;
