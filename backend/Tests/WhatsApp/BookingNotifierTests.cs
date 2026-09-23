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
    private sealed class OkSender : ITwilioSender
    {
        public Task<string> SendAsync(string to, string body, CancellationToken ct)
            => Task.FromResult("SM-TEST-1");
    }

    private sealed class BoomSender : ITwilioSender
    {
        public Task<string> SendAsync(string to, string body, CancellationToken ct)
            => throw new HttpRequestException("Twilio down");
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notifier-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedAsync(AppDbContext db, string? phone = "+447700900123")
    {
        var user = new User { Name = "Thandi", Email = "thandi@example.com", PhoneE164 = phone, TimeZoneId = "Europe/London" };
        var slot = new Slot { Date = new DateOnly(2026, 10, 6), MeetLink = "https://meet.google.com/x" };
        var booking = new BookingEntity { SlotId = slot.Id, StudentId = user.Id };
        db.Users.Add(user);
        db.Slots.Add(slot);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    [Fact]
    public async Task Success_writes_sent_row_with_sid()
    {
        using var db = NewDb();
        var id = await SeedAsync(db);
        var notifier = new BookingNotifier(db, new OkSender(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("sent", row.Result);
        Assert.Equal("SM-TEST-1", row.TwilioSid);
        Assert.Equal("confirmation", row.Template);
        Assert.Equal("+447700900123", row.To);
    }

    [Fact]
    public async Task Failing_sender_writes_failed_row_and_never_throws()
    {
        using var db = NewDb();
        var id = await SeedAsync(db);
        var notifier = new BookingNotifier(db, new BoomSender(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Cancelled, CancellationToken.None);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal("failed", row.Result);
        Assert.Equal("cancelled", row.Template);
    }

    [Fact]
    public async Task Missing_phone_sends_nothing()
    {
        using var db = NewDb();
        var id = await SeedAsync(db, phone: null);
        var notifier = new BookingNotifier(db, new OkSender(), NullLogger<BookingNotifier>.Instance);

        await notifier.NotifyBookingChangedAsync(id, BookingChangeKind.Created, CancellationToken.None);

        Assert.Empty(db.ReminderLogs);
    }
}
