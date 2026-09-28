using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tests.Auth;

[Collection("Api")]
public class GoogleAuthFlowTests
{
    private readonly ApiFactory _factory;
    public GoogleAuthFlowTests(ApiFactory factory) => _factory = factory;

    private static async Task LoginAsTeacherAsync(HttpClient client)
    {
        await client.PostAsJsonAsync("/api/auth/request-code", new { email = "teacher@example.com" });
        var code = ApiFactory.LastCode;
        var verify = await client.PostAsJsonAsync("/api/auth/verify",
            new { email = "teacher@example.com", code });
        verify.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Start_redirects_to_google_for_admin()
    {
        using var googleFactory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Google:ClientId", "test-client-id");
            b.UseSetting("Google:ClientSecret", "test-secret");
            b.UseSetting("Google:RedirectUri", "http://localhost/api/auth/google/callback");
        });
        var client = googleFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsTeacherAsync(client);

        var res = await client.GetAsync("/api/auth/google/start");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location?.ToString() ?? "";
        Assert.Contains("accounts.google.com", location);
        const string ownedScope = "https://www.googleapis.com/auth/calendar.events.owned";
        Assert.Contains($"scope={Uri.EscapeDataString(ownedScope)}&access_type=offline", location);
        Assert.DoesNotContain(
            $"scope={Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events")}&access_type=offline",
            location);
    }

    [Fact]
    public async Task Start_anonymous_is_401()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.GetAsync("/api/auth/google/start");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Status_unconnected_for_fresh_db()
    {
        var client = _factory.CreateClient();
        await LoginAsTeacherAsync(client);

        var res = await client.GetAsync("/api/admin/google/status");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<GoogleStatus>();
        Assert.NotNull(body);
        Assert.False(body.Connected);
        Assert.False(body.NeedsReconnect);
    }

    [Fact]
    public async Task Disconnect_anonymous_is_401()
    {
        var client = _factory.CreateClient();

        var res = await client.DeleteAsync("/api/admin/google");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Disconnect_student_is_403()
    {
        var student = _factory.CreateClient();
        await student.PostAsJsonAsync("/api/auth/request-code", new { email = "studenta@example.com" });
        var verify = await student.PostAsJsonAsync(
            "/api/auth/verify",
            new { email = "studenta@example.com", code = ApiFactory.LastCode });
        verify.EnsureSuccessStatusCode();

        var res = await student.DeleteAsync("/api/admin/google");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Disconnect_admin_without_token_reports_remote_revoked()
    {
        using var googleFactory = _factory.WithWebHostBuilder(b =>
            b.UseSetting("Google:TokenKey", Convert.ToBase64String(new byte[32])));
        var client = googleFactory.CreateClient();
        await LoginAsTeacherAsync(client);

        var res = await client.DeleteAsync("/api/admin/google");
        var body = await res.Content.ReadFromJsonAsync<GoogleDisconnectResponse>();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.RemoteRevoked);
    }

    private sealed record GoogleDisconnectResponse(bool RemoteRevoked);

    private static readonly string ValidTokenKey =
        Convert.ToBase64String(new byte[32]);

    private sealed record GoogleStatus(
        bool Connected, bool NeedsReconnect, string MeetProvider, bool EventsEnabled);

    // Regression: production ran with the Meet provider left at its "fixed"
    // default while the admin UI showed Google as connected. The account really
    // was connected, but bookings silently used the fallback link and created no
    // calendar event, and nothing distinguished the two states.
    [Fact]
    public async Task Status_reports_events_disabled_in_fixed_mode()
    {
        var client = _factory.CreateClient();
        await LoginAsTeacherAsync(client);

        var body = await client.GetFromJsonAsync<GoogleStatus>("/api/admin/google/status");

        Assert.NotNull(body);
        Assert.Equal("fixed", body!.MeetProvider);
        Assert.False(body.EventsEnabled);
    }

    [Fact]
    public async Task Status_reports_events_enabled_in_google_mode()
    {
        // google mode validates Google:TokenKey at DI time and throws loudly
        // without it, so supply a valid 32-byte key.
        using var googleFactory = _factory.WithWebHostBuilder(b => b
            .UseSetting("App:Meet:Provider", "google")
            .UseSetting("Google:TokenKey", ValidTokenKey));
        var client = googleFactory.CreateClient();
        await LoginAsTeacherAsync(client);

        var body = await client.GetFromJsonAsync<GoogleStatus>("/api/admin/google/status");

        Assert.NotNull(body);
        Assert.Equal("google", body!.MeetProvider);
        Assert.True(body.EventsEnabled);
    }

    [Fact]
    public async Task Google_mode_is_case_insensitive()
    {
        using var googleFactory = _factory.WithWebHostBuilder(b => b
            .UseSetting("App:Meet:Provider", "Google")
            .UseSetting("Google:TokenKey", ValidTokenKey));
        var client = googleFactory.CreateClient();
        await LoginAsTeacherAsync(client);

        var body = await client.GetFromJsonAsync<GoogleStatus>("/api/admin/google/status");

        Assert.Equal("google", body!.MeetProvider);
        Assert.True(body.EventsEnabled);
    }
}
