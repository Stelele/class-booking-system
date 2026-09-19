using Booking.Domain.Slots;
using Xunit;

public class LessonTimeTests
{
    [Fact]
    public void StartUtc_is_1830Z_for_any_harare_date() // CAT = UTC+2 year-round
    {
        var utc = LessonTime.StartUtc(new DateOnly(2026, 9, 22));
        Assert.Equal(new DateTime(2026, 9, 22, 18, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void EndUtc_is_two_hours_later()
    {
        Assert.Equal(LessonTime.StartUtc(new DateOnly(2026, 9, 22)).AddHours(2),
                     LessonTime.EndUtc(new DateOnly(2026, 9, 22)));
    }
}
