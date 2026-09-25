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

    private sealed record GoogleStatus(bool Connected, bool NeedsReconnect);
}
