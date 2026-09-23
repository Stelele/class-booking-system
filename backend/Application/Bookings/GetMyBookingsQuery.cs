using Application.Abstractions;
using Application.DTOs;

namespace Application.Bookings;

public sealed record GetMyBookingsQuery : IQuery<List<BookingDto>>;
