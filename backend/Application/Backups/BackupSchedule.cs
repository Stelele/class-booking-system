namespace Application.Backups;

public static class BackupSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public static DateTime NextRunUtc(DateTime nowUtc)
    {
        var slots = (nowUtc.Ticks / Interval.Ticks) + 1;
        return new DateTime(slots * Interval.Ticks, DateTimeKind.Utc);
    }
}
