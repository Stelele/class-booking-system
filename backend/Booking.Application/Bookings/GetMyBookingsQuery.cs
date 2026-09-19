using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Bookings;

public sealed record GetMyBookingsQuery : IQuery<List<BookingDto>>;
