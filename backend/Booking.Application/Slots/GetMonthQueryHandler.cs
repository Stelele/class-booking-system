using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Slots;

public sealed class GetMonthQueryHandler(IAppDbContext db) : IQueryHandler<GetMonthQuery, List<SlotDayDto>>
{
    public async Task<List<SlotDayDto>> Handle(GetMonthQuery q, CancellationToken ct)
    {
        var first = new DateOnly(q.Year, q.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var blockedSet = (await db.BlockedDays
                .Where(b => b.Date >= first && b.Date <= last)
                .Select(b => b.Date).ToListAsync(ct)).ToHashSet();

        var slots = await db.Slots
            .Include(s => s.Bookings)
            .Where(s => s.Date >= first && s.Date <= last)
            .ToListAsync(ct);

        var activeByDate = slots
            .SelectMany(s => s.Bookings.Where(b => b.Status == BookingStatus.Active)
                .Select(b => (s.Date, b.StudentId)))
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.StudentId).ToList());

        var names = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var now = DateTime.UtcNow;
        var days = new List<SlotDayDto>();

        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var (state, reason) = Classify(d, blockedSet, now, activeByDate.GetValueOrDefault(d));
            var studentIds = activeByDate.GetValueOrDefault(d) ?? [];
            days.Add(new SlotDayDto(
                Date: d,
                StartUtc: LessonTime.StartUtc(d),
                EndUtc: LessonTime.EndUtc(d),
                State: state,
                CanBook: state == DayState.Bookable,
                Reason: reason,
                StudentNames: studentIds.Select(id => names.GetValueOrDefault(id, "?")).ToList(),
                MeetLink: slots.FirstOrDefault(s => s.Date == d)?.MeetLink));
        }
        return days;
    }

    internal static (DayState, string?) Classify(DateOnly d, HashSet<DateOnly> blocked, DateTime now, List<Guid>? activeIds)
    {
        if (d.DayOfWeek == DayOfWeek.Sunday) return (DayState.Sunday, "No lessons on Sundays");
        if (blocked.Contains(d)) return (DayState.Blocked, "Teacher unavailable");
        if (now >= LessonTime.StartUtc(d)) return (DayState.Past, "Lesson already passed");
        if (now > LessonTime.StartUtc(d).AddMinutes(-BookingPolicy.CutoffMinutes))
            return (DayState.Cutoff, "Booking window closed");
        return activeIds switch
        {
            null or [] => (DayState.Bookable, null),
            { Count: 1 } => (DayState.Booked, null),
            _ => (DayState.Combined, null)
        };
    }
}
