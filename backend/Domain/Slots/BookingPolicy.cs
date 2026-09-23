namespace Domain.Slots;

public static class BookingPolicy
{
    public const int CutoffMinutes = 30;

    /// Returns null when bookable, else a human-readable error string.
    public static string? Validate(DateOnly date, DateTime nowUtc, IReadOnlyCollection<DateOnly> blockedDays)
    {
        if (date.DayOfWeek == DayOfWeek.Sunday)
            return "Sundays are off — no lessons on Sundays.";
        if (blockedDays.Contains(date))
            return "Teacher is unavailable that day.";
        if (nowUtc >= LessonTime.StartUtc(date))
            return "That lesson is in the past.";
        if (nowUtc > LessonTime.StartUtc(date).AddMinutes(-CutoffMinutes))
            return $"Too late — bookings close {CutoffMinutes} minutes before the lesson starts.";
        return null;
    }
}
