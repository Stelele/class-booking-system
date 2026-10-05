using System.Globalization;
using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using TimeZoneConverter;

namespace Application.Bookings;

/// <summary>
/// The teacher's upcoming lessons, each with its students and join link.
/// Admin only: the row carries other students' names and the meeting URL.
/// </summary>
public sealed record ListAdminLessonsQuery : IQuery<List<TeacherLessonDto>>;

public sealed class ListAdminLessonsQueryHandler(IAppDbContext db, ICurrentUser user)
    : IQueryHandler<ListAdminLessonsQuery, List<TeacherLessonDto>>
{
    public async Task<List<TeacherLessonDto>> Handle(ListAdminLessonsQuery q, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");

        var now = DateTime.UtcNow;
        // Compare dates in the lesson's own zone, not UTC: Harare is UTC+2, so
        // between 00:00 and 02:00 local "today" is still yesterday in UTC and a
        // UTC-date floor would drop tonight's lesson from the list.
        var harareNow = TimeZoneInfo.ConvertTimeFromUtc(now, TZConvert.GetTimeZoneInfo(LessonTime.ZoneId));
        var earliest = DateOnly.FromDateTime(harareNow).AddDays(-1);

        // Bookings are loaded with their slot so one query covers the whole list.
        // Only active ones count — a cancelled student must not appear on the
        // lesson, and a day with no active booking has no lesson to teach.
        var rows = await db.Bookings.AsNoTracking()
            .Include(b => b.Slot)
            .Where(b => b.Status == BookingStatus.Active && b.Slot.Date >= earliest)
            .ToListAsync(ct);

        // The DB filter is a coarse bound; a lesson that has already finished is
        // not one the teacher can join, so drop it on the actual end time. The
        // extra day back lets a still-running lesson survive the zone skew above.
        rows = [.. rows.Where(b => LessonTime.EndUtc(b.Slot.Date) > now)];

        var studentIds = rows.Select(b => b.StudentId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var tz = TZConvert.GetTimeZoneInfo(LessonTime.ZoneId);

        return rows
            // Group by date, not by the Slot entity: EF materialises a separate
            // Slot instance per booking, so grouping by the reference splits one
            // combined lesson into two rows with one student each.
            .GroupBy(b => b.Slot.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var date = g.Key;
                var count = g.Count();
                return new TeacherLessonDto(
                    Date: date,
                    StartUtc: LessonTime.StartUtc(date),
                    EndUtc: LessonTime.EndUtc(date),
                    LocalTime: TimeZoneInfo
                        .ConvertTimeFromUtc(LessonTime.StartUtc(date), tz)
                        .ToString("HH:mm", CultureInfo.InvariantCulture),
                    // The booking id travels with the name so the teacher can act
                    // on one student's booking without touching the other's.
                    Students: g
                        .Select(b => new TeacherLessonStudentDto(
                            b.Id, names.GetValueOrDefault(b.StudentId, "?")))
                        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    MeetLink: g.First().Slot.MeetLink,
                    IsCombined: count > 1,
                    CanCancel: true,
                    CanReschedule: true);
            })
            .ToList();
    }
}