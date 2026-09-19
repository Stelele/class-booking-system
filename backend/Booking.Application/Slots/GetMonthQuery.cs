using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Slots;

public sealed record GetMonthQuery(int Year, int Month) : IQuery<List<SlotDayDto>>;
