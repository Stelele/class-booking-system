using Application.Backups;

namespace Tests.Backups;

public class BackupScheduleTests
{
    [Fact]
    public void NextRunUtc_snaps_to_the_next_quarter_hour()
    {
        var now = new DateTime(2026, 9, 24, 10, 7, 30, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 24, 10, 15, 0, DateTimeKind.Utc), BackupSchedule.NextRunUtc(now));
    }

    [Fact]
    public void NextRunUtc_from_exact_boundary_rolls_forward_so_no_zero_delay_loop()
    {
        var now = new DateTime(2026, 9, 24, 10, 15, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 24, 10, 30, 0, DateTimeKind.Utc), BackupSchedule.NextRunUtc(now));
    }

    [Fact]
    public void NextRunUtc_rolls_over_midnight()
    {
        var now = new DateTime(2026, 9, 24, 23, 52, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), BackupSchedule.NextRunUtc(now));
    }

    [Fact]
    public void NextRunUtc_is_always_strictly_in_the_future_across_a_full_day()
    {
        var start = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 96; i++)
        {
            var now = start.AddMinutes(i * 15);
            Assert.True(BackupSchedule.NextRunUtc(now) > now, $"slot not in the future for {now:O}");
        }
    }
}
