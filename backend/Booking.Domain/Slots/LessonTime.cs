using TimeZoneConverter;

namespace Booking.Domain.Slots;

public static class LessonTime
{
    public const string ZoneId = "Africa/Harare";
    public const int DurationHours = 2;

    public static DateTime StartUtc(DateOnly date)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        var local = date.ToDateTime(new TimeOnly(20, 30));
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
    }

    public static DateTime EndUtc(DateOnly date) => StartUtc(date).AddHours(DurationHours);
}
