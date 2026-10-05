using Application.Abstractions;
using Application.Notifications;
using Domain.Slots;
using Domain.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Domain.Slots.Booking;

public class BookingNotifierTests
{
    private sealed class OkNotifier : INotifier
    {
        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
            => Task.FromResult(new NotifyResult("push"));
    }

    private sealed class EmailNotifierStub : INotifier
    {
        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
            => Task.FromResult(new NotifyResult("email", "resend-1"));
    }

    private sealed class BoomNotifier : INotifier
    {
        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
            => throw new HttpRequestException("notification transport down");
    }

    private sealed record Sent(Guid UserId, string Title, string Body, NotifyUrgency Urgency);

    /// Succeeds for the student, then throws for the teacher. BoomNotifier throws
    /// on the first send, so a test using it never reaches the teacher branch at
    /// all and passes without exercising it.
    /// Fails for the student, succeeds for the teacher — the mirror image of
    /// <see cref="TeacherFailsNotifier"/>. Exercises the path where one
    /// recipient's failure must not suppress the other's delivery.
    private sealed class StudentFailsNotifier(Guid studentId) : INotifier
    {
        public int Calls { get; private set; }
        public List<Sent> Sent { get; } = [];

        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
        {
            Calls++;
            if (userId == studentId)
                throw new HttpRequestException("student channel down");
            Sent.Add(new Sent(userId, title, body, urgency));
            return Task.FromResult(new NotifyResult("push"));
        }
    }

    private sealed class TeacherFailsNotifier(Guid teacherId) : INotifier
    {
        public int Calls { get; private set; }

        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
        {
            Calls++;
            if (userId == teacherId)
                throw new HttpRequestException("teacher channel down");
            return Task.FromResult(new NotifyResult("push"));
        }
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<Sent> Sent { get; } = [];

        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
        {
            Sent.Add(new Sent(userId, title, body, urgency));
            return Task.FromResult(new NotifyResult("push"));
        }
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notifier-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Guid BookingId, Guid UserId)> SeedAsync(
        AppDbContext db, string? phone = "+447700900123")
    {
        var user = new User { Name = "Thandi", Email = "thandi@example.com", PhoneE164 = phone, TimeZoneId = "Europe/London" };
        var slot = new Slot { Date = new DateOnly(2026, 10, 6), MeetLink = "https://meet.google.com/x" };
        var booking = new BookingEntity { SlotId = slot.Id, StudentId = user.Id };
        db.Users.Add(user);
        db.Slots.Add(slot);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (booking.Id, user.Id);
    }

    // The teacher is the only person who can teach the lesson, so a new booking
    // is useless to them unless they are told about it.
    private static async Task<Guid> SeedTeacherAsync(AppDbContext db, string email = "teacher@example.com")
    {
        var teacher = new User
        {
            Name = "Gift",
            Email = email,
            PhoneE164 = "+447700900999",
            TimeZoneId = "Africa/Harare",
            Role = UserRole.Admin,
        };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        return teacher.Id;
    }

    [Fact]
    public async Task Success_writes_sent_row_with_channel_and_user()
    {
        using var db = NewDb();
        var (id, userId) = await SeedAsync(db);
        var notifier = new BookingNotifier(db, new OkNotifier(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("sent", row.Result);
        Assert.Equal("push", row.Channel);
        Assert.Equal(userId, row.UserId);
        Assert.Equal("thandi@example.com", row.To);
        Assert.Equal("confirmation", row.Template);
    }

    [Fact]
    public async Task Email_fallback_records_provider_ref()
    {
        using var db = NewDb();
        var (id, _) = await SeedAsync(db);
        var notifier = new BookingNotifier(db, new EmailNotifierStub(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("email", row.Channel);
        Assert.Equal("resend-1", row.ProviderRef);
    }

    [Fact]
    public async Task Failing_sender_writes_failed_row_and_never_throws()
    {
        using var db = NewDb();
        var (id, userId) = await SeedAsync(db);
        var notifier = new BookingNotifier(db, new BoomNotifier(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Cancelled, CancellationToken.None);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("failed", row.Result);
        Assert.Equal("cancelled", row.Template);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task User_without_a_phone_still_gets_notified()
    {
        using var db = NewDb();
        var (id, userId) = await SeedAsync(db, phone: null);
        var notifier = new BookingNotifier(db, new OkNotifier(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        // Push and email need no phone number — this is the behaviour change
        // that removed the old "no phone, no notification" guard.
        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("sent", row.Result);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task New_booking_notifies_the_teacher_with_student_and_link()
    {
        using var db = NewDb();
        var (id, _) = await SeedAsync(db);
        var teacherId = await SeedTeacherAsync(db);
        var notifier = new RecordingNotifier();
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        var toTeacher = Assert.Single(notifier.Sent, s => s.UserId == teacherId);
        Assert.Equal("New lesson booked", toTeacher.Title);
        Assert.Contains("Thandi", toTeacher.Body);
        Assert.Contains("https://meet.google.com/x", toTeacher.Body);
        Assert.Equal(NotifyUrgency.Normal, toTeacher.Urgency);
    }

    // The teacher is in Harare, not the student's zone — the message must use
    // the teacher's own wall clock or they turn up an hour early.
    [Fact]
    public async Task Teacher_message_uses_harare_time()
    {
        using var db = NewDb();
        var (id, _) = await SeedAsync(db);
        var teacherId = await SeedTeacherAsync(db);
        var notifier = new RecordingNotifier();
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        var toTeacher = Assert.Single(notifier.Sent, s => s.UserId == teacherId);
        Assert.Contains("20:30", toTeacher.Body); // 20:30 Africa/Harare
    }

    // Cancelling is the student's own business; the teacher does not need a copy.
    [Fact]
    public async Task Cancellation_does_not_notify_the_teacher()
    {
        using var db = NewDb();
        var (id, _) = await SeedAsync(db);
        var teacherId = await SeedTeacherAsync(db);
        var notifier = new RecordingNotifier();
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(id, BookingChangeKind.Cancelled, CancellationToken.None);

        Assert.DoesNotContain(notifier.Sent, s => s.UserId == teacherId);
    }

    // A teacher who books themselves (testing their own site) must not get the
    // same message twice.
    [Fact]
    public async Task Teacher_who_booked_themselves_is_notified_once()
    {
        using var db = NewDb();
        var teacher = new User
        {
            Name = "Gift",
            Email = "teacher@example.com",
            PhoneE164 = "+447700900999",
            TimeZoneId = "Africa/Harare",
            Role = UserRole.Admin,
        };
        var slot = new Slot { Date = new DateOnly(2026, 10, 6), MeetLink = "https://meet.google.com/x" };
        var booking = new BookingEntity { SlotId = slot.Id, StudentId = teacher.Id };
        db.Users.Add(teacher);
        db.Slots.Add(slot);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var notifier = new RecordingNotifier();
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Created, CancellationToken.None);

        Assert.Single(notifier.Sent);
    }


    // A broken teacher notification must not take down the student's, or worse,
    // fail the booking that triggered it. The stub succeeds for the student and
    // throws for the teacher, so the teacher branch is genuinely reached —
    // a stub that throws on the first send returns early and never gets here.
    [Fact]
    public async Task Teacher_notification_failure_never_throws()
    {
        using var db = NewDb();
        var (id, userId) = await SeedAsync(db);
        var teacherId = await SeedTeacherAsync(db);
        var notifier = new TeacherFailsNotifier(teacherId);
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        var ex = await Record.ExceptionAsync(
            () => sut.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None));

        Assert.Null(ex);
        Assert.Equal(2, notifier.Calls); // the teacher send was actually attempted
        var rows = await db.ReminderLogs.Where(r => r.UserId == userId).ToListAsync();
        Assert.Contains(rows, r => r.Template == "confirmation" && r.Result == "sent");
    }

    // A student-side delivery failure must not swallow the teacher alert: the
    // booking was saved, and the teacher is the one person who can teach it.
    [Fact]
    public async Task Teacher_is_notified_even_when_the_student_notification_fails()
    {
        using var db = NewDb();
        var (id, userId) = await SeedAsync(db);
        var teacherId = await SeedTeacherAsync(db);
        var notifier = new StudentFailsNotifier(userId);
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        // the student send failed...
        var rows = await db.ReminderLogs.ToListAsync();
        Assert.Contains(rows, r => r.UserId == userId && r.Template == "confirmation" && r.Result == "failed");
        // ...and the teacher was still told
        Assert.Equal(2, notifier.Calls);
        var toTeacher = Assert.Single(notifier.Sent);
        Assert.Equal(teacherId, toTeacher.UserId);
        Assert.Equal("New lesson booked", toTeacher.Title);
    }

    // Suppression must key on the new booking's own student, not on the date:
    // a teacher who booked the day must not silence another student's alert.
    [Fact]
    public async Task Teacher_holding_the_day_still_hears_about_another_students_booking()
    {
        using var db = NewDb();
        var teacher = new User
        {
            Name = "Gift",
            Email = "teacher@example.com",
            TimeZoneId = "Africa/Harare",
            Role = UserRole.Admin,
        };
        var first = new User { Name = "Thandi", Email = "thandi@example.com", TimeZoneId = "Europe/London" };
        var second = new User { Name = "Rudo", Email = "rudo@example.com", TimeZoneId = "Europe/London" };
        var slot = new Slot { Date = new DateOnly(2026, 10, 6), MeetLink = "https://meet.google.com/x" };
        var teacherBooking = new BookingEntity { SlotId = slot.Id, StudentId = teacher.Id };
        var firstBooking = new BookingEntity { SlotId = slot.Id, StudentId = first.Id };
        var secondBooking = new BookingEntity { SlotId = slot.Id, StudentId = second.Id };
        db.Users.AddRange(teacher, first, second);
        db.Slots.Add(slot);
        db.Bookings.AddRange(teacherBooking, firstBooking, secondBooking);
        await db.SaveChangesAsync();

        var notifier = new RecordingNotifier();
        var sut = new BookingNotifier(db, notifier, NullLogger<BookingNotifier>.Instance);

        await sut.NotifyBookingChangedAsync(
            secondBooking.Id, BookingChangeKind.Created, CancellationToken.None);

        Assert.Contains(notifier.Sent, s => s.UserId == second.Id);
        var toTeacher = Assert.Single(notifier.Sent, s => s.UserId == teacher.Id);
        Assert.Equal("New lesson booked", toTeacher.Title);
        Assert.Contains("Rudo", toTeacher.Body);
    }
}
