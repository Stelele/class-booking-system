using System.Net;
using Application.Abstractions;
using Domain.Reminders;
using Domain.Users;
using Infrastructure.Auth;
using Infrastructure.Notifications;
using Infrastructure.Persistence;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using StoredSubscription = Domain.Reminders.PushSubscription;

public class PushNotifierTests
{
    // ---- test doubles -------------------------------------------------

    internal sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public byte[]? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    /// <summary>Records whether the fallback fired, without a Resend key.</summary>
    internal sealed class RecordingEmailNotifier(bool enabled) : EmailNotifier(
        new StubFactory(), Options.Create(new EmailHttpOptions { ApiKey = enabled ? "k" : "" }),
        NullLogger<EmailNotifier>.Instance)
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }

        public override Task<NotifyResult> SendToAsync(
            string email, string name, string title, string body, CancellationToken ct)
        {
            Calls++;
            LastBody = body;
            return Task.FromResult(new NotifyResult("email", "resend-1"));
        }
    }

    /// <summary>Used only by the email stub, which overrides the send path.</summary>
    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>Hands the push client a handler that returns a fixed status.</summary>
    private sealed class PushFactory(HttpStatusCode status) : IHttpClientFactory
    {
        public StubHandler Handler { get; } = new(status);
        public HttpClient CreateClient(string name) => new(Handler);
    }

    // ---- fixtures -----------------------------------------------------

    private static async Task<(AppDbContext Db, User User)> SeedAsync(
        int subscriptions = 1, bool isIos = false)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"push-{Guid.NewGuid():N}")
            .Options;
        var db = new AppDbContext(options);
        var user = new User { Name = "Thandi", Email = "thandi@example.com", TimeZoneId = "Europe/London" };
        db.Users.Add(user);
        for (var i = 0; i < subscriptions; i++)
        {
            db.PushSubscriptions.Add(new StoredSubscription
            {
                UserId = user.Id,
                Endpoint = $"https://fcm.googleapis.com/fcm/send/abc{i}",
                P256Dh = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U",
                Auth = "8eDyX_uCN0XRhSbY5hs7Hg",
                IsIos = isIos,
            });
        }
        await db.SaveChangesAsync();
        return (db, user);
    }

    private static (WebPushSender Sender, PushFactory Factory) PushSender(
        AppDbContext db, HttpStatusCode status)
    {
        var factory = new PushFactory(status);
        return (BuildPushSender(db, factory), factory);
    }

    private static WebPushSender BuildPushSender(AppDbContext db, IHttpClientFactory factory)
    {
        // Generate once — the two halves must come from the same pair.
        var keys = VapidKeys.Generate();
        return new WebPushSender(
            db,
            factory,
            Options.Create(new PushOptions
            {
                VapidPublicKey = keys.PublicKey,
                VapidPrivateKey = keys.PrivateKey,
                VapidSubject = "mailto:test@example.com",
            }),
            new MemoryVapidTokenCache(),
            NullLogger<WebPushSender>.Instance);
    }

    private static WebPushSender DisabledPushSender(AppDbContext db) => new(
        db,
        new StubFactory(),
        Options.Create(new PushOptions()),
        new MemoryVapidTokenCache(),
        NullLogger<WebPushSender>.Instance);

    // ---- push delivery ------------------------------------------------

    [Fact]
    public async Task Accepted_push_reports_delivered()
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var (sender, factory) = PushSender(db, HttpStatusCode.Created);

        var outcome = await sender.SendAsync(user.Id, "Lesson booked", "Hi Thandi", NotifyUrgency.Normal, default);

        Assert.Equal(1, outcome.Delivered);
        Assert.Equal(0, outcome.Pruned);
        // The real library must have signed VAPID and encrypted the payload.
        Assert.NotNull(factory.Handler.LastRequest);
        Assert.Contains("vapid", factory.Handler.LastRequest!.Headers.Authorization?.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(factory.Handler.LastBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Dead_endpoint_is_pruned(HttpStatusCode status)
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var (sender, _) = PushSender(db, status);

        var outcome = await sender.SendAsync(user.Id, "Lesson booked", "Hi", NotifyUrgency.Normal, default);

        Assert.Equal(1, outcome.Pruned);
        Assert.Empty(await db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Transient_failure_keeps_the_subscription()
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.InternalServerError);

        var outcome = await sender.SendAsync(user.Id, "Lesson booked", "Hi", NotifyUrgency.Normal, default);

        Assert.Equal(0, outcome.Delivered);
        Assert.Equal(0, outcome.Pruned);
        Assert.Single(await db.PushSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Unconfigured_push_is_inert()
    {
        var (db, _) = await SeedAsync();
        using var _unused = db;
        var sender = DisabledPushSender(db);

        var outcome = await sender.SendAsync(Guid.NewGuid(), "T", "B", NotifyUrgency.Normal, default);

        Assert.False(sender.Enabled);
        Assert.Equal(0, outcome.Delivered);
    }

    // ---- push + email fallback ---------------------------------------

    [Fact]
    public async Task Delivered_push_does_not_email()
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.Created);
        var email = new RecordingEmailNotifier(enabled: true);
        var notifier = new PushOrEmailNotifier(db, sender, email, NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Lesson booked", "Hi", NotifyUrgency.Normal, default);

        Assert.Equal("push", result.Channel);
        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task No_subscription_falls_back_to_email()
    {
        var (db, user) = await SeedAsync(subscriptions: 0);
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.Created);
        var email = new RecordingEmailNotifier(enabled: true);
        var notifier = new PushOrEmailNotifier(db, sender, email, NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Starting soon", "Lesson in 30 min", NotifyUrgency.High, default);

        Assert.Equal("email", result.Channel);
        Assert.Equal("resend-1", result.ProviderRef);
        Assert.Equal(1, email.Calls);
        Assert.Equal("Lesson in 30 min", email.LastBody);
    }

    [Fact]
    public async Task Failed_push_still_falls_back_to_email()
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.InternalServerError);
        var email = new RecordingEmailNotifier(enabled: true);
        var notifier = new PushOrEmailNotifier(db, sender, email, NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Starting soon", "Lesson in 30 min", NotifyUrgency.Normal, default);

        Assert.Equal("email", result.Channel);
        Assert.Equal(1, email.Calls);
    }

    [Fact]
    public async Task Neither_channel_configured_is_log_only()
    {
        var (db, user) = await SeedAsync();
        using var _ = db;
        var notifier = new PushOrEmailNotifier(
            db, DisabledPushSender(db), new RecordingEmailNotifier(enabled: false),
            NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Lesson booked", "Hi", NotifyUrgency.Normal, default);

        Assert.Equal("log", result.Channel);
    }

    // ---- iOS cannot confirm display, so it also gets the email ---------

    [Fact]
    public async Task Ios_subscription_also_receives_the_email()
    {
        var (db, user) = await SeedAsync(subscriptions: 1, isIos: true);
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.Created);
        var email = new RecordingEmailNotifier(enabled: true);
        var notifier = new PushOrEmailNotifier(db, sender, email, NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Starting soon", "Lesson in 30 min", NotifyUrgency.Normal, default);

        // Push succeeded at the API level, but iOS display is unverifiable, so
        // the reminder must not rest on push alone.
        Assert.Equal("email", result.Channel);
        Assert.Equal(1, email.Calls);
        Assert.Equal("Lesson in 30 min", email.LastBody);
    }

    [Fact]
    public async Task Non_ios_subscription_still_uses_push_only()
    {
        var (db, user) = await SeedAsync(subscriptions: 1, isIos: false);
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.Created);
        var email = new RecordingEmailNotifier(enabled: true);
        var notifier = new PushOrEmailNotifier(db, sender, email, NullLogger<PushOrEmailNotifier>.Instance);

        var result = await notifier.SendAsync(user.Id, "Starting soon", "Lesson in 30 min", NotifyUrgency.Normal, default);

        Assert.Equal("push", result.Channel);
        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task Ios_device_count_is_reported_per_send()
    {
        var (db, user) = await SeedAsync(subscriptions: 2, isIos: true);
        using var _ = db;
        var (sender, _) = PushSender(db, HttpStatusCode.Created);

        var outcome = await sender.SendAsync(user.Id, "T", "B", NotifyUrgency.Normal, default);

        Assert.Equal(2, outcome.Delivered);
        Assert.Equal(2, outcome.IosDevices);
    }

    // ---- email body rendering ----------------------------------------

    [Fact]
    public void Monday_summary_newlines_survive_as_br()
    {
        Assert.Equal("line one<br>line two", EmailNotifier.BodyHtml("line one\nline two"));
    }

    [Fact]
    public void Html_in_a_name_is_escaped()
    {
        Assert.DoesNotContain("<script>", EmailNotifier.BodyHtml("<script>alert(1)</script>"));
    }
}
