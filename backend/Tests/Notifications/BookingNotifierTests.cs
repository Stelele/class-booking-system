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
}
