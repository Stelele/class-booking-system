using Application.Abstractions;
using Application.DTOs;

namespace Application.Slots;

public sealed record GetMonthQuery(int Year, int Month) : IQuery<List<SlotDayDto>>;
