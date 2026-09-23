using Application.Notifications;
using Xunit;

public class ReminderScheduleTests
{
    [Fact]
    public void Morning_is_0600Z_and_evening_1800Z_for_harare_date()
    {
        // 2026-10-06: Harare is UTC+2 year-round → 08:00+02 = 06:00Z, 20:00+02 = 18:00Z
        var fires = ReminderSchedule.ForLesson(new DateOnly(2026, 10, 6));
        Assert.Equal(2, fires.Count);
        Assert.Equal(new DateTime(2026, 10, 6, 6, 0, 0, DateTimeKind.Utc), fires[0].Utc);
        Assert.Equal("morning", fires[0].Template);
        Assert.Equal(new DateTime(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc), fires[1].Utc);
        Assert.Equal("evening", fires[1].Template);
    }

    [Fact]
    public void Monday_summary_is_monday_0700Z()
    {
        // Monday 09:00+02 = 07:00Z; now = Sunday 2026-10-04 12:00Z
        var next = ReminderSchedule.NextMondaySummaryUtc(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Student_time_flips_with_BST()
    {
        // lesson 20:30 Harare = 18:30Z year-round
        Assert.Equal("19:30", ReminderSchedule.StudentLocalTime(new DateOnly(2026, 9, 22))); // BST
        Assert.Equal("18:30", ReminderSchedule.StudentLocalTime(new DateOnly(2026, 12, 3))); // GMT
    }

    [Fact]
    public void Monday_summary_skips_today_if_past_0900()
    {
        // Monday 2026-10-05 08:00Z = 10:00 Harare → next Monday 07:00Z
        var next = ReminderSchedule.NextMondaySummaryUtc(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc), next);
    }
}
