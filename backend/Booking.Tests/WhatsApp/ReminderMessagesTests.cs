using Booking.Application.Notifications;
using Xunit;

public class ReminderMessagesTests
{
    [Fact]
    public void Confirmation_names_day_time_and_link()
    {
        var msg = ReminderMessages.Confirmation("Thandi", new DateOnly(2026, 10, 6), "19:30", "https://meet.google.com/x");
        Assert.Contains("Thandi", msg);
        Assert.Contains("19:30", msg);
        Assert.Contains("https://meet.google.com/x", msg);
    }

    [Fact]
    public void Monday_summary_lists_each_lesson_line()
    {
        var msg = ReminderMessages.MondaySummary("Thandi",
            [(new DateOnly(2026, 10, 6), "19:30"), (new DateOnly(2026, 10, 8), "19:30")]);
        Assert.Contains("Tue", msg);
        Assert.Contains("Thu", msg);
    }

    [Fact]
    public void Nudges_carry_times_and_links()
    {
        Assert.Contains("30 min", ReminderMessages.EveningNudge("Thandi", "19:30", "https://meet.google.com/x"));
        Assert.Contains("tonight", ReminderMessages.MorningNudge("Thandi", "19:30", "https://meet.google.com/x"));
    }
}
