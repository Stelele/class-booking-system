using Application.Abstractions;
using Domain.Reminders;
using Domain.Slots;
using Domain.Users;
using Infrastructure.Notifications;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BookingEntity = Domain.Slots.Booking;

/// <summary>
/// The scheduler's idempotency key changed from (phone, date, template) to
/// (userId, date, template) when notifications stopped requiring a phone
/// number. A wrong key here means every student re-receives reminders they
/// already got.
/// </summary>
public class ReminderServiceTests
{
    private sealed class RecordingNotifier : INotifier
    {
        public List<(Guid UserId, string Title, NotifyUrgency Urgency)> Sent { get; } = [];
        public NotifyResult Result { get; set; } = new("push");

        public Task<NotifyResult> SendAsync(
            Guid userId, string title, string body,
            NotifyUrgency urgency = NotifyUrgency.Normal, CancellationToken ct = default)
        {
            Sent.Add((userId, title, urgency));
            return Task.FromResult(Result);
        }
    }

    private static (AppDbContext Db, IServiceScopeFactory Scopes, RecordingNotifier Notifier) Harness()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"sched-{Guid.NewGuid():N}")
            .Options;
        var db = new AppDbContext(options);

        var notifier = new RecordingNotifier();
        var services = new ServiceCollection()
            .AddSingleton<IAppDbContext>(db)
            .AddSingleton<INotifier>(notifier)
            .BuildServiceProvider();
        return (db, services.GetRequiredService<IServiceScopeFactory>(), notifier);
    }

    private static IConfiguration Config(bool monday = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reminder:Monday"] = monday ? "true" : "false",
                ["Reminder:Morning"] = "true",
                ["Reminder:Evening"] = "true",
            })
            .Build();

    private static async Task<(Guid UserId, DateOnly Date)> SeedAsync(AppDbContext db, DateOnly date)
    {
        var user = new User
        {
            Name = "Thandi", Email = "thandi@example.com",
            PhoneE164 = null, // push needs no phone number
            TimeZoneId = "Europe/London",
        };
        var slot = new Slot { Date = date, MeetLink = "https://meet.google.com/x" };
        db.Users.Add(user);
        db.Slots.Add(slot);
        await db.SaveChangesAsync();
        db.Bookings.Add(new BookingEntity { SlotId = slot.Id, StudentId = user.Id });
        await db.SaveChangesAsync();
        return (user.Id, date);
    }

    private static ReminderService Service(IServiceScopeFactory scopes, IConfiguration? config = null) =>
        new(scopes, config ?? Config(), NullLogger<ReminderService>.Instance);

    [Fact]
    public async Task A_user_without_a_phone_still_gets_lesson_nudges()
    {
        var (db, scopes, _) = Harness();
        using var _ = db;
        var (_, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        var service = Service(scopes);

        // window covering the 06:00Z morning nudge
        var fires = await service.CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), default);

        var morning = Assert.Single(fires, f => f.Template == "morning");
        Assert.Equal(date, morning.LessonDate);
    }

    [Fact]
    public async Task Both_nudges_fire_in_one_evening_window()
    {
        var (db, scopes, _) = Harness();
        using var _ = db;
        await SeedAsync(db, new DateOnly(2026, 10, 6));

        var fires = await Service(scopes).CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), default);

        Assert.Equal(2, fires.Count);
        Assert.Contains(fires, f => f.Template == "morning");
        Assert.Contains(fires, f => f.Template == "evening");
    }

    [Fact]
    public async Task A_sent_reminder_is_not_collected_again()
    {
        var (db, scopes, _) = Harness();
        using var _ = db;
        var (userId, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        db.ReminderLogs.Add(new ReminderLog
        {
            UserId = userId, To = "thandi@example.com", Date = date,
            Template = "morning", Result = "sent", Channel = "push",
        });
        await db.SaveChangesAsync();

        var fires = await Service(scopes).CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), default);

        Assert.DoesNotContain(fires, f => f.Template == "morning");
    }

    [Fact]
    public async Task A_failed_reminder_is_retried()
    {
        var (db, scopes, _) = Harness();
        using var _ = db;
        var (userId, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        db.ReminderLogs.Add(new ReminderLog
        {
            UserId = userId, To = "thandi@example.com", Date = date,
            Template = "morning", Result = "failed",
        });
        await db.SaveChangesAsync();

        var fires = await Service(scopes).CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), default);

        Assert.Single(fires, f => f.Template == "morning");
    }

    [Fact]
    public async Task Legacy_rows_with_no_user_id_do_not_suppress_a_reminder()
    {
        // Pre-migration rows keyed on the phone number and have UserId = null.
        // They must not block a user who has since opted in.
        var (db, scopes, _) = Harness();
        using var _ = db;
        var (_, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        db.ReminderLogs.Add(new ReminderLog
        {
            UserId = null, To = "+447700900123", Date = date,
            Template = "morning", Result = "sent",
        });
        await db.SaveChangesAsync();

        var fires = await Service(scopes).CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), default);

        Assert.Single(fires, f => f.Template == "morning");
    }

    [Fact]
    public async Task The_evening_nudge_is_high_urgency_and_the_morning_is_not()
    {
        var (db, scopes, notifier) = Harness();
        using var _ = db;
        var (_, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        var service = Service(scopes);

        var fires = await service.CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), default);
        foreach (var fire in fires) await service.SendFireAsync(fire, default);

        Assert.Equal(NotifyUrgency.Normal, notifier.Sent.Single(s => s.Title == "Tonight's lesson").Urgency);
        Assert.Equal(NotifyUrgency.High, notifier.Sent.Single(s => s.Title == "Starting soon").Urgency);
    }

    [Fact]
    public async Task Sending_records_the_user_id_and_channel()
    {
        var (db, scopes, notifier) = Harness();
        using var _ = db;
        var (userId, date) = await SeedAsync(db, new DateOnly(2026, 10, 6));
        notifier.Result = new NotifyResult("email", "resend-9");
        var service = Service(scopes);

        var fires = await service.CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), default);
        await service.SendFireAsync(fires.Single(), default);

        var row = await db.ReminderLogs.SingleAsync();
        Assert.Equal(userId, row.UserId);
        Assert.Equal("thandi@example.com", row.To);
        Assert.Equal(date, row.Date);
        Assert.Equal("email", row.Channel);
        Assert.Equal("resend-9", row.ProviderRef);
    }

    [Fact]
    public async Task A_cancelled_booking_produces_no_nudges()
    {
        var (db, scopes, _) = Harness();
        using var _ = db;
        var user = new User { Name = "Ana", Email = "ana@example.com", TimeZoneId = "Europe/London" };
        var slot = new Slot { Date = new DateOnly(2026, 10, 6) };
        db.Users.Add(user);
        db.Slots.Add(slot);
        await db.SaveChangesAsync();
        db.Bookings.Add(new BookingEntity
        {
            SlotId = slot.Id, StudentId = user.Id,
            Status = BookingStatus.Cancelled,
        });
        await db.SaveChangesAsync();

        var fires = await Service(scopes).CollectDueFiresAsync(
            db, new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), default);

        Assert.Empty(fires);
    }
}
