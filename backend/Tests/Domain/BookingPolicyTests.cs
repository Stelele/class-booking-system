using Domain.Slots;
using Xunit;

public class BookingPolicyTests
{
    private static readonly DateOnly Future = new(2026, 12, 3); // a Thursday

    [Fact]
    public void Free_future_weekday_is_bookable()
        => Assert.Null(BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddDays(-1), []));

    [Fact]
    public void Sunday_is_rejected()
        => Assert.Contains("Sunday", BookingPolicy.Validate(new DateOnly(2026, 12, 6),
            LessonTime.StartUtc(new DateOnly(2026, 12, 6)).AddDays(-1), []));

    [Fact]
    public void Blocked_day_is_rejected()
        => Assert.Contains("unavailable", BookingPolicy.Validate(Future,
            LessonTime.StartUtc(Future).AddDays(-1), [Future]));

    [Fact]
    public void Past_day_is_rejected()
        => Assert.Contains("past", BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddDays(1), []));

    [Fact]
    public void Within_30min_cutoff_is_rejected()
        => Assert.Contains("30", BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddMinutes(-29), []));

    [Fact]
    public void Exactly_30min_before_is_allowed()
        => Assert.Null(BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddMinutes(-30), []));
}
