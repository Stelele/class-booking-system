using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Application.Auth;
using Domain.Users;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Auth;

[Collection("Api")]
public class GoogleLoginFlowTests
{
    private const string LoginRedirect = "http://localhost/api/auth/google/login/callback";
    private const string FailureRedirect = "/login?google=error";
    private const string TeacherEmail = "teacher@example.com";
    private const string AppCookiePrefix = ".AspNetCore.Cookies=";
    private const string StateCookieName = "GoogleLoginState";

    private readonly ApiFactory _factory;
    public GoogleLoginFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Start_requests_only_openid_email_profile_and_login_redirect()
    {
        using var app = App(new FakeLoginService(Verified(TeacherEmail, "Google Person")));

        var res = await app.Client.GetAsync("/api/auth/google/login/start");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location?.ToString() ?? "";
        Assert.Contains("accounts.google.com", location);
        var query = QueryHelpers.ParseQuery(new Uri(location).Query);
        Assert.Equal(GoogleLoginScopes.OpenIdEmailProfile, query["scope"].ToString());
        Assert.Equal(LoginRedirect, query["redirect_uri"].ToString());
        Assert.DoesNotContain("calendar.events", location);
    }

    [Fact]
    public async Task Start_sets_http_only_state_cookie_holding_only_the_binding()
    {
        using var app = App(new FakeLoginService(Verified(TeacherEmail, "Google Person")));

        var res = await app.Client.GetAsync("/api/auth/google/login/start");

        Assert.True(res.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var header = setCookies!.Single(h => h.StartsWith(StateCookieName + "=", StringComparison.Ordinal));
        var parts = header.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());
        Assert.Contains(parts, p => p.Equals("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(parts, p => p.StartsWith("samesite=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(parts, p => p.Equals("path=/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parts, p => p.Contains("ya29."));
        Assert.DoesNotContain(parts, p => p.Contains("refresh_token", StringComparison.OrdinalIgnoreCase));
        var value = header.Split(';')[0][(StateCookieName.Length + 1)..];
        Assert.Equal(43, value.Length);
        Assert.DoesNotContain('+', value);
        Assert.DoesNotContain('/', value);
    }

    [Fact]
    public async Task Start_without_google_configuration_redirects_to_login_error()
    {
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        using var app = App(service, clientId: "", loginRedirectUri: "");

        var res = await app.Client.GetAsync("/api/auth/google/login/start");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Equal(0, service.Calls);
        Assert.Null(AppAuthCookie(res));
    }

    [Fact]
    public async Task Callback_with_invalid_state_redirects_to_login_error()
    {
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        using var app = App(service);

        var res = await app.Client.GetAsync(
            "/api/auth/google/login/callback?code=auth-code&state=not-a-real-state");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Equal(0, service.Calls);
        Assert.Null(AppAuthCookie(res));
    }

    [Fact]
    public async Task Callback_without_code_redirects_to_login_error()
    {
        using var app = App(new FakeLoginService(Verified(TeacherEmail, "Google Person")));

        var res = await app.Client.GetAsync("/api/auth/google/login/callback?state=abc");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Null(AppAuthCookie(res));
    }

    [Fact]
    public async Task Callback_without_state_cookie_redirects_to_login_error()
    {
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        using var app = App(service);
        var state = await StateAsync(app.Client);
        var other = app.CreateClient();

        var res = await other.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Equal(0, service.Calls);
        Assert.Null(AppAuthCookie(res));
    }

    [Fact]
    public async Task Callback_with_unverified_email_redirects_to_login_error()
    {
        using var app = App(new FakeLoginService(
            new GoogleLoginIdentity("sub-1", "person@example.com", false, "Unverified")));
        var state = await StateAsync(app.Client);

        var res = await app.Client.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Null(AppAuthCookie(res));
        Assert.True(ClearsStateCookie(res));
    }

    [Fact]
    public async Task Callback_with_unknown_verified_email_redirects_and_creates_no_user()
    {
        var service = new FakeLoginService(Verified("nobody@example.com", "Nobody"));
        using var app = App(service);
        var state = await StateAsync(app.Client);

        var res = await app.Client.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(FailureRedirect, res.Headers.Location?.ToString());
        Assert.Null(AppAuthCookie(res));
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, db.Users.Count());
        Assert.False(db.Users.Any(u => u.Email == "nobody@example.com"));
    }

    [Fact]
    public async Task Callback_issues_cookie_and_redirects_to_calendar()
    {
        using var app = App(new FakeLoginService(Verified(TeacherEmail, "Google Person")));
        var state = await StateAsync(app.Client);

        var res = await app.Client.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/calendar", res.Headers.Location?.ToString());
        var cookie = AppAuthCookie(res);
        Assert.NotNull(cookie);
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", cookie!);
        var meResponse = await app.Client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var user = await meResponse.Content.ReadFromJsonAsync<UserMe>();
        Assert.Equal("Teacher", user!.Name);
    }

    [Fact]
    public async Task Callback_state_is_single_use()
    {
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        using var app = App(service);
        var state = await StateAsync(app.Client);

        var first = await app.Client.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");
        var second = await app.Client.GetAsync(
            $"/api/auth/google/login/callback?code=auth-code&state={Uri.EscapeDataString(state)}");

        Assert.Equal("/calendar", first.Headers.Location?.ToString());
        Assert.Equal(FailureRedirect, second.Headers.Location?.ToString());
        Assert.Null(AppAuthCookie(second));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Complete_handler_uses_database_name_and_role_not_google_profile()
    {
        await using var db = NewDb();
        var states = new StubStateStore();
        var handler = new CompleteGoogleLoginCommandHandler(
            states, new FakeLoginService(Verified(" Teacher@Example.com ", "Google Person")), db);

        var user = await handler.Handle(
            new CompleteGoogleLoginCommand("auth-code", states.State, states.Binding), CancellationToken.None);

        Assert.Equal("Teacher", user.Name);
        Assert.Equal("teacher@example.com", user.Email);
        Assert.Equal("Admin", user.Role);
    }

    [Fact]
    public async Task Complete_handler_rejects_unverified_email()
    {
        await using var db = NewDb();
        var states = new StubStateStore();
        var handler = new CompleteGoogleLoginCommandHandler(
            states, new FakeLoginService(
                new GoogleLoginIdentity("sub-1", TeacherEmail, false, "Unverified")), db);

        await Assert.ThrowsAsync<AuthException>(() => handler.Handle(
            new CompleteGoogleLoginCommand("auth-code", states.State, states.Binding), CancellationToken.None));
    }

    [Fact]
    public async Task Complete_handler_rejects_invalid_state_before_calling_google()
    {
        await using var db = NewDb();
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        var states = new StubStateStore();
        var handler = new CompleteGoogleLoginCommandHandler(states, service, db);

        await Assert.ThrowsAsync<AuthException>(() => handler.Handle(
            new CompleteGoogleLoginCommand("auth-code", "forged", states.Binding), CancellationToken.None));

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Complete_handler_rejects_mismatched_binding_before_calling_google()
    {
        await using var db = NewDb();
        var service = new FakeLoginService(Verified(TeacherEmail, "Google Person"));
        var states = new StubStateStore();
        var handler = new CompleteGoogleLoginCommandHandler(states, service, db);

        await Assert.ThrowsAsync<AuthException>(() => handler.Handle(
            new CompleteGoogleLoginCommand("auth-code", states.State, "someone-elses-binding"),
            CancellationToken.None));

        Assert.Equal(0, service.Calls);
    }

    private TestApp App(
        IGoogleLoginService service,
        string clientId = "test-client-id",
        string loginRedirectUri = LoginRedirect)
    {
        var googleFactory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Google:ClientId", clientId);
            b.UseSetting("Google:ClientSecret", "test-secret");
            b.UseSetting("Google:RedirectUri", "http://localhost/api/auth/google/callback");
            b.UseSetting("Google:LoginRedirectUri", loginRedirectUri);
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleLoginService>();
                services.AddScoped<IGoogleLoginService>(_ => service);
            });
        });
        var client = googleFactory.CreateClient(ClientOptions());
        return new TestApp(googleFactory, client);
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    private static string? AppAuthCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) return null;
        return setCookies
            .Where(h => h.StartsWith(AppCookiePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Split(';', StringSplitOptions.RemoveEmptyEntries).First().Trim())
            .FirstOrDefault();
    }

    private static bool ClearsStateCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies)) return false;
        return setCookies.Any(h => CookiePair(h) == StateCookieName + "=");
    }

    private static string CookiePair(string setCookieHeader) =>
        setCookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    private static async Task<string> StateAsync(HttpClient client)
    {
        var start = await client.GetAsync("/api/auth/google/login/start");
        var location = Uri.UnescapeDataString(start.Headers.Location?.ToString() ?? "");
        const string marker = "state=";
        var index = location.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "" : location[(index + marker.Length)..].Split('&')[0];
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Users.Add(new User
        {
            Name = "Teacher",
            Email = TeacherEmail,
            Role = UserRole.Admin,
            TimeZoneId = "Africa/Harare",
        });
        db.SaveChanges();
        return db;
    }

    private static GoogleLoginIdentity Verified(string email, string name)
        => new("sub-1", email, true, name);

    private sealed record TestApp(WebApplicationFactory<Program> Factory, HttpClient Client) : IDisposable
    {
        public HttpClient CreateClient() => Factory.CreateClient(ClientOptions());

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed class FakeLoginService(GoogleLoginIdentity identity) : IGoogleLoginService
    {
        public int Calls { get; private set; }

        public Task<GoogleLoginIdentity> AuthenticateAsync(string code, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(identity);
        }
    }

    private sealed class StubStateStore : IGoogleOAuthStateStore
    {
        public string State { get; } = "issued-state";
        public string Binding { get; } = "";
        public string Issue(string purpose, string binding = "") => State;
        public bool Consume(string state, string purpose, string binding = "")
            => state == State && purpose == "login" && binding == Binding;
    }

    public sealed record UserMe(Guid Id, string Name, string Email, string Role);
}
