using Domain.Slots;
using TimeZoneConverter;

namespace Application.Notifications;

public static class ReminderSchedule
{
    public const string ZoneId = "Africa/Harare";
    public const string StudentZoneId = "Europe/London";

    public sealed record FireTime(DateTime Utc, string Template, DateOnly LessonDate);

    /// All fire times (UTC) for one lesson date: 08:00 + 20:00 Harare same day.
    public static List<FireTime> ForLesson(DateOnly date)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        DateTime AtUtc(int h, int m) => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(h, m)), DateTimeKind.Unspecified), tz);
        return
        [
            new(AtUtc(8, 0), "morning", date),
            new(AtUtc(20, 0), "evening", date),
        ];
    }

    /// Next Monday 09:00 Harare in UTC.
    public static DateTime NextMondaySummaryUtc(DateTime nowUtc)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        var harareNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)harareNow.DayOfWeek + 7) % 7;
        var candidate = harareNow.Date.AddDays(daysToMonday).AddHours(9);
        if (candidate <= harareNow) candidate = candidate.AddDays(7);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), tz);
    }

    /// Student-local "19:30" text for a lesson date (BST-safe).
    public static string StudentLocalTime(DateOnly date)
    {
        var studentTz = TZConvert.GetTimeZoneInfo(StudentZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(LessonTime.StartUtc(date), studentTz);
        return local.ToString("HH:mm");
    }
}
