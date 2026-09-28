using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Api;

[Collection("Api")]
public class PushSubscriptionTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly List<Guid> _added = [];

    public async Task InitializeAsync() => await Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        foreach (var sub in await db.PushSubscriptions.Where(s => _added.Contains(s.UserId)).ToListAsync())
            db.PushSubscriptions.Remove(sub);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/request-code", new { email });
        var res = await client.PostAsJsonAsync("/api/auth/verify", new { email, code = ApiFactory.LastCode });
        res.EnsureSuccessStatusCode();
        return client;
    }

    private static object Req(string endpoint, bool isIos = false) => new
    {
        endpoint,
        p256dh = PushTestKeys.P256Dh(),
        auth = PushTestKeys.Auth(),
        isIos,
    };

    [Fact]
    public async Task Anonymous_cannot_register_a_subscription()
    {
        var anon = factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/notifications/push-subscription",
            Req("https://fcm.googleapis.com/fcm/send/abc"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Recognised_push_service_endpoint_is_accepted()
    {
        var client = await LoginAsync("studenta@example.com");
        _added.Add(StudentId());

        var res = await client.PostAsJsonAsync("/api/notifications/push-subscription",
            Req("https://fcm.googleapis.com/fcm/send/accepted-1"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Subdomain_of_an_allowed_push_service_is_accepted()
    {
        var client = await LoginAsync("studenta@example.com");
        _added.Add(StudentId());

        var res = await client.PostAsJsonAsync("/api/notifications/push-subscription",
            Req("https://wns2-bl2-wus3.notify.windows.com/w/?token=abc"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]        // not https
    [InlineData("https://evil.example.com/steal")]                 // not a push service
    [InlineData("https://fcm.googleapis.com.evil.example.com/x")]  // suffix lookalike
    [InlineData("https://evilfcm.googleapis.com/x")]               // prefix lookalike
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    public async Task Non_push_service_endpoint_is_rejected(string endpoint)
    {
        var client = await LoginAsync("studenta@example.com");

        var res = await client.PostAsJsonAsync("/api/notifications/push-subscription", Req(endpoint));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Re_subscribing_the_same_endpoint_does_not_duplicate()
    {
        var client = await LoginAsync("studenta@example.com");
        _added.Add(StudentId());
        const string endpoint = "https://fcm.googleapis.com/fcm/send/dup-1";

        await client.PostAsJsonAsync("/api/notifications/push-subscription", Req(endpoint));
        await client.PostAsJsonAsync("/api/notifications/push-subscription", Req(endpoint));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        Assert.Equal(1, await db.PushSubscriptions.CountAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task Delete_removes_the_callers_subscriptions()
    {
        var client = await LoginAsync("studenta@example.com");
        await client.PostAsJsonAsync("/api/notifications/push-subscription",
            Req("https://fcm.googleapis.com/fcm/send/to-delete"));

        var res = await client.DeleteAsync("/api/notifications/push-subscription");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        Assert.Empty(await db.PushSubscriptions
            .Where(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/to-delete").ToListAsync());
    }

    [Fact]
    public async Task Push_key_endpoint_reports_enabled_false_without_configuration()
    {
        // No VAPID key is configured in the test host.
        var client = await LoginAsync("studenta@example.com");
        var res = await client.GetFromJsonAsync<PushKeyResponse>("/api/notifications/push-key",
            BookingApiTests.ApiJson);

        Assert.NotNull(res);
        Assert.False(res!.Enabled);
    }

    private sealed record PushKeyResponse(bool Enabled, string? VapidPublicKey);

    [Fact]
    public async Task Is_ios_flag_is_persisted()
    {
        var client = await LoginAsync("studenta@example.com");
        _added.Add(StudentId());

        await client.PostAsJsonAsync("/api/notifications/push-subscription",
            Req("https://fcm.googleapis.com/fcm/send/ios-1", isIos: true));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        var row = await db.PushSubscriptions.FirstAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/ios-1");
        Assert.True(row.IsIos);
    }

    [Fact]
    public async Task Omitting_the_ios_flag_defaults_to_false()
    {
        var client = await LoginAsync("studenta@example.com");
        _added.Add(StudentId());

        await client.PostAsJsonAsync("/api/notifications/push-subscription", new
        {
            endpoint = "https://fcm.googleapis.com/fcm/send/no-flag",
            p256dh = PushTestKeys.P256Dh(),
            auth = PushTestKeys.Auth(),
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        var row = await db.PushSubscriptions.FirstAsync(s => s.Endpoint == "https://fcm.googleapis.com/fcm/send/no-flag");
        Assert.False(row.IsIos);
    }

    private Guid StudentId()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IAppDbContext>();
        return db.Users.AsNoTracking().Single(u => u.Email == "studenta@example.com").Id;
    }
}
