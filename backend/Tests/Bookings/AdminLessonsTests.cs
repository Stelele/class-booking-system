using System.Security.Cryptography;
using Application.Abstractions;
using Application.Bookings;
using Domain.Slots;
using Domain.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TimeZoneConverter;
using Xunit;

namespace Tests.Bookings;

// The teacher has no bookings of their own, so /mine shows them nothing. These
// cover the admin listing that gives them their lessons and the join link.
public class AdminLessonsTests
{
    private sealed record StubCurrentUser(Guid UserId, bool IsAdmin = false) : ICurrentUser
    {
        public string Name => "Teacher";
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"admin-lessons-{Guid.NewGuid():N}")
            .Options);

    private static async Task<User> AddStudentAsync(
        AppDbContext db, string name, string? email = null)
    {
        var user = new User
        {
            Name = name,
            Email = email ?? $"{name.ToLowerInvariant()}@example.com",
            TimeZoneId = "Europe/London",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Slot> AddLessonAsync(
        AppDbContext db,
        DateOnly date,
        IEnumerable<User> students,
        string meetLink = "https://meet.google.com/abc-defg-hij")
    {
        var slot = new Slot { Date = date, MeetLink = meetLink, GoogleEventId = "evt_1" };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();
        foreach (var student in students)
        {
            db.Bookings.Add(new Booking { SlotId = slot.Id, StudentId = student.Id });
        }
        await db.SaveChangesAsync();
        return slot;
    }

    private static DateOnly Future(int daysAhead) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);

    [Fact]
    public async Task Returns_upcoming_lessons_with_students_and_link()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var date = Future(3);
        await AddLessonAsync(db, date, [thandi]);

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Equal(date, lesson.Date);
        Assert.Equal(["Thandi"], lesson.StudentNames);
        Assert.Equal(1, lesson.StudentCount);
        Assert.Equal("https://meet.google.com/abc-defg-hij", lesson.MeetLink);
        Assert.Equal("20:30", lesson.LocalTime);
        Assert.Equal(LessonTime.StartUtc(date), lesson.StartUtc);
        Assert.Equal(LessonTime.EndUtc(date), lesson.EndUtc);
    }

    [Fact]
    public async Task Two_students_make_a_combined_lesson()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var rudo = await AddStudentAsync(db, "Rudo");
        await AddLessonAsync(db, Future(4), [thandi, rudo]);

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Equal(2, lesson.StudentCount);
        Assert.True(lesson.IsCombined);
        Assert.Equal(2, lesson.StudentNames.Count);
    }

    // The teacher acts on one student's booking. Cancelling the lesson must
    // never take the other student with them, so each entry needs its own id.
    [Fact]
    public async Task Each_student_carries_their_own_booking_id()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var rudo = await AddStudentAsync(db, "Rudo");
        var slot = await AddLessonAsync(db, Future(11), [thandi, rudo]);

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Equal(2, lesson.Students.Count);
        var thandiRow = Assert.Single(lesson.Students, s => s.Name == "Thandi");
        var rudoRow = Assert.Single(lesson.Students, s => s.Name == "Rudo");
        Assert.NotEqual(thandiRow.BookingId, rudoRow.BookingId);
        Assert.Equal(
            slot.Bookings.Single(b => b.StudentId == thandi.Id).Id, thandiRow.BookingId);
        Assert.Equal(
            slot.Bookings.Single(b => b.StudentId == rudo.Id).Id, rudoRow.BookingId);
    }

    // Both actions are permitted because an admin may act on any booking.
    [Fact]
    public async Task Every_lesson_is_cancellable_and_reschedulable()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        await AddLessonAsync(db, Future(12), [thandi]);

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.True(lesson.CanCancel);
        Assert.True(lesson.CanReschedule);
    }

    // A lesson in the past is not something the teacher can join.
    [Fact]
    public async Task Excludes_past_and_empty_days()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        await AddLessonAsync(db, Future(-2), [thandi], "https://meet.google.com/past");
        await AddLessonAsync(db, Future(5), [thandi], "https://meet.google.com/future");
        db.Slots.Add(new Slot { Date = Future(6), MeetLink = "https://meet.google.com/nobody" });
        await db.SaveChangesAsync();

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Equal(Future(5), lesson.Date);
    }

    // The lesson runs 20:30-22:30 Harare (18:30-20:30 UTC). Whichever side of
    // that window the test executes in, the row must agree with the clock —
    // filtering on the date alone would list a lesson that has already ended.
    [Fact]
    public async Task Includes_todays_lesson_only_while_it_is_still_running()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        await AddLessonAsync(db, Future(0), [thandi], "https://meet.google.com/tonight");

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var shouldBeListed = DateTime.UtcNow < LessonTime.EndUtc(Future(0));
        Assert.Equal(shouldBeListed, result.Any(l => l.Date == Future(0)));
    }

    // A cancelled student is not on the lesson, so naming them would send the
    // teacher to the wrong room size.
    [Fact]
    public async Task Excludes_cancelled_bookings_from_student_names()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var rudo = await AddStudentAsync(db, "Rudo");
        var slot = await AddLessonAsync(db, Future(7), [thandi, rudo]);
        foreach (var booking in slot.Bookings.Where(b => b.StudentId == rudo.Id))
            booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Equal(["Thandi"], lesson.StudentNames);
        Assert.False(lesson.IsCombined);
    }

    [Fact]
    public async Task Sorts_chronologically()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        await AddLessonAsync(db, Future(9), [thandi]);
        await AddLessonAsync(db, Future(4), [thandi]);
        await AddLessonAsync(db, Future(6), [thandi]);

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        Assert.Equal(result.Select(l => l.Date).Order(), result.Select(l => l.Date));
        Assert.Equal([Future(4), Future(6), Future(9)], result.Select(l => l.Date));
    }

    [Fact]
    public async Task Is_empty_when_nothing_is_booked()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    // Admin-only: this list exposes every student's name and join link.
    [Fact]
    public async Task Student_cannot_list_all_lessons()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var rudo = await AddStudentAsync(db, "Rudo");
        await AddLessonAsync(db, Future(8), [rudo]);

        var handler = new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(new ListAdminLessonsQuery(), CancellationToken.None));
    }

    // A lesson can exist before Google is connected (fixed-link mode), and the
    // teacher still needs to see it — just without a link to join.
    [Fact]
    public async Task Lesson_without_a_meet_link_is_still_listed()
    {
        await using var db = NewDb();
        var thandi = await AddStudentAsync(db, "Thandi");
        var date = Future(10);
        var slot = new Slot { Date = date };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking { SlotId = slot.Id, StudentId = thandi.Id });
        await db.SaveChangesAsync();

        var result = await new ListAdminLessonsQueryHandler(db, new StubCurrentUser(thandi.Id, true))
            .Handle(new ListAdminLessonsQuery(), CancellationToken.None);

        var lesson = Assert.Single(result);
        Assert.Null(lesson.MeetLink);
        Assert.Equal(1, lesson.StudentCount);
    }
}