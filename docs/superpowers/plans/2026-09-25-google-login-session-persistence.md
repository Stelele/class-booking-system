# Google Login and Persistent Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add existing-user Google login and preserve 30-day app sessions across ordinary deployments.

**Architecture:** Reuse the existing Google OAuth client through a separate server-side login flow that requests only `openid email profile`, exchanges the code on the backend, verifies the Google email, and matches it to an existing `User`. Keep Calendar authorization separate. Persist ASP.NET Data Protection keys under the existing `/app/data` volume so the current cookie remains valid when the container is replaced.

**Tech Stack:** .NET 10, ASP.NET Core cookie authentication, EF Core SQLite, Google OAuth 2.0/OpenID Connect, Vue 3, Nuxt UI, Playwright, Docker Compose, GitHub Actions

---

## Execution Preconditions

- Work in `.worktrees/google-oauth-ci-rollout` on `fix/google-oauth-ci-rollout`.
- The approved design is committed at `docs/superpowers/specs/2026-09-25-google-login-session-persistence-design.md`.
- This plan is uncommitted until the user explicitly authorizes its commit.
- Obtain explicit authorization before implementation commits and before pushing to `origin/main`.
- Preserve the existing Calendar scope `calendar.events.owned`; login must never request Calendar access.
- The current production Calendar grant was revoked and its active local token deleted during the previous rollout. Reconnect it only after the new login flow is deployed and verified.
- Add `GOOGLE_LOGIN_REDIRECT_URI` without printing its value.
- Do not add Auth0, a client-side Google SDK, a client secret to frontend code, or a new account table.
- Do not add comments to production or test code.

## File Map

| File | Responsibility |
|---|---|
| `backend/Host/Program.cs` | Persist Data Protection keys and map the login endpoints |
| `backend/Application/Abstractions/GoogleLoginIdentity.cs` | Server-side Google identity result |
| `backend/Application/Abstractions/IGoogleLoginService.cs` | Server-side Google login boundary |
| `backend/Application/Abstractions/GoogleOAuthSettings.cs` | Add the login callback URI |
| `backend/Application/Abstractions/GoogleOAuthScopes.cs` | Own the login-only scope string |
| `backend/Application/Auth/BeginGoogleLoginQuery.cs` | Start-login request |
| `backend/Application/Auth/BeginGoogleLoginQueryHandler.cs` | Build the Google login URL |
| `backend/Application/Auth/CompleteGoogleLoginCommand.cs` | Callback request |
| `backend/Application/Auth/CompleteGoogleLoginCommandHandler.cs` | Validate identity and match an existing user |
| `backend/Endpoints/AuthCookie.cs` | Issue the same app cookie for email and Google login |
| `backend/Endpoints/GoogleLoginEndpoints.cs` | Login start/callback routes |
| `backend/Infrastructure/Google/GoogleOAuthClient.cs` | Exchange code and fetch Google user info |
| `backend/Infrastructure/Google/GoogleLoginService.cs` | Coordinate code exchange and user-info lookup |
| `backend/Infrastructure/DependencyInjection.cs` | Register login service and settings |
| `backend/Tests/Auth/PersistentSessionTests.cs` | Verify key-ring persistence and cookie continuity |
| `backend/Tests/Auth/GoogleLoginFlowTests.cs` | Verify login URLs, callback behavior, and roles |
| `backend/Tests/Google/GoogleOAuthClientTests.cs` | Verify bearer user-info request and response mapping |
| `frontend/src/composables/useAuth.ts` | Start the server-side Google login redirect |
| `frontend/src/views/LoginView.vue` | Render Google login and sanitized callback error |
| `e2e/playwright.config.ts` | Supply the local login callback setting for E2E |
| `e2e/tests/booking.spec.ts` | Verify Google login UI and email fallback |
| `infra/docker-compose.yml` | Pass the login callback URI to the backend |
| `infra/Program.cs` | Pass the new environment variable through deployment |
| `.github/workflows/deploy.yml` | Supply the login callback secret to Pulumi |
| `docs/superpowers/specs/2026-09-25-google-login-session-persistence-design.md` | Approved design reference |

### Task 0: Commit the approved implementation plan

- [ ] **Step 1: Obtain explicit authorization**

Ask before committing the plan document.

- [ ] **Step 2: Commit documentation only**

```bash
git add docs/superpowers/plans/2026-09-25-google-login-session-persistence.md
git commit -m "docs: add Google login implementation plan"
```

Expected: the commit contains only the plan document.

### Task 1: Persist Data Protection keys across container replacement

**Files:**
- Modify: `backend/Host/Program.cs:1-25`
- Create: `backend/Tests/Auth/PersistentSessionTests.cs`

- [ ] **Step 1: Write the failing persistence test**

Create `backend/Tests/Auth/PersistentSessionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Tests.Auth;

[Collection("Api")]
public sealed class PersistentSessionTests
{
    private readonly ApiFactory _factory;

    public PersistentSessionTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Email_cookie_survives_host_restart_with_same_data_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "booking.db");
        Directory.CreateDirectory(root);

        try
        {
            using var first = _factory.WithWebHostBuilder(b => b.UseSetting("App:DbPath", dbPath));
            var firstClient = first.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
            var setCookie = await LoginAsTeacherAsync(firstClient);

            var keyDirectory = Path.Combine(root, "data-protection-keys");
            Assert.True(Directory.Exists(keyDirectory));
            Assert.NotEmpty(Directory.GetFiles(keyDirectory));

            first.Dispose();

            using var second = _factory.WithWebHostBuilder(b => b.UseSetting("App:DbPath", dbPath));
            var secondClient = second.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
            secondClient.DefaultRequestHeaders.Add("Cookie", setCookie);

            var response = await secondClient.GetAsync("/api/auth/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<string> LoginAsTeacherAsync(HttpClient client)
    {
        await client.PostAsJsonAsync("/api/auth/request-code", new { email = "teacher@example.com" });
        var verify = await client.PostAsJsonAsync(
            "/api/auth/verify",
            new { email = "teacher@example.com", code = ApiFactory.LastCode });
        verify.EnsureSuccessStatusCode();
        return verify.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~PersistentSessionTests"
```

Expected: FAIL because the current application does not create `/data-protection-keys` under the configured data directory.

- [ ] **Step 3: Configure the persistent key ring**

In `backend/Host/Program.cs`, add:

```csharp
using Microsoft.AspNetCore.DataProtection;
```

After `builder.Services.ConfigureHttpJsonOptions(...)` and before `AddInfrastructure`, add:

```csharp
var configuredDbPath = builder.Configuration["App:DbPath"] ?? "data/booking.db";
var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(configuredDbPath))!;
var keyDirectory = Path.Combine(dataDirectory, "data-protection-keys");
Directory.CreateDirectory(keyDirectory);
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("ClassBooking");
```

Keep the existing cookie options unchanged, including `ExpireTimeSpan = TimeSpan.FromDays(30)` and `IsPersistent = true` in the authentication properties.

- [ ] **Step 4: Run the persistence test and verify GREEN**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~PersistentSessionTests"
```

Expected: PASS; the key directory exists and the same cookie authenticates against a second host using the same data directory.

- [ ] **Step 5: Run the existing backend suite**

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass with 0 failures.

- [ ] **Step 6: Commit after explicit authorization**

```bash
git add backend/Host/Program.cs backend/Tests/Auth/PersistentSessionTests.cs
git commit -m "fix(auth): persist session protection keys"
```

Expected: one commit containing only the two listed files.

### Task 2: Add the server-side Google login flow

**Files:**
- Create: `backend/Application/Abstractions/GoogleLoginIdentity.cs`
- Create: `backend/Application/Abstractions/IGoogleLoginService.cs`
- Modify: `backend/Application/Abstractions/GoogleOAuthSettings.cs`
- Create: `backend/Application/Abstractions/GoogleLoginScopes.cs`
- Create: `backend/Application/Auth/BeginGoogleLoginQuery.cs`
- Create: `backend/Application/Auth/BeginGoogleLoginQueryHandler.cs`
- Create: `backend/Application/Auth/CompleteGoogleLoginCommand.cs`
- Create: `backend/Application/Auth/CompleteGoogleLoginCommandHandler.cs`
- Create: `backend/Endpoints/AuthCookie.cs`
- Create: `backend/Endpoints/GoogleLoginEndpoints.cs`
- Modify: `backend/Endpoints/AuthEndpoints.cs`
- Modify: `backend/Host/Program.cs`
- Modify: `backend/Infrastructure/Google/GoogleOAuthClient.cs`
- Create: `backend/Infrastructure/Google/GoogleLoginService.cs`
- Modify: `backend/Infrastructure/DependencyInjection.cs`
- Modify: `backend/Tests/Google/GoogleOAuthClientTests.cs`
- Create: `backend/Tests/Auth/GoogleLoginFlowTests.cs`

- [ ] **Step 1: Write failing Google client tests**

Add `using Application.Abstractions;` to `backend/Tests/Google/GoogleOAuthClientTests.cs`, then add a user-info client and test:

```csharp
    [Fact]
    public async Task UserInfo_sends_bearer_and_maps_verified_identity()
    {
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer ya29.new", request.Headers.Authorization?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        sub = "google-subject-123",
                        email = "teacher@example.com",
                        email_verified = true,
                        name = "Teacher",
                    }),
                    Encoding.UTF8,
                    "application/json"),
            };
        }))
        {
            BaseAddress = new Uri("https://oauth2.googleapis.com/"),
        };

        var identity = await new GoogleOAuthClient(http).GetUserInfoAsync(
            "ya29.new", CancellationToken.None);

        Assert.Equal("google-subject-123", identity.Subject);
        Assert.Equal("teacher@example.com", identity.Email);
        Assert.True(identity.EmailVerified);
        Assert.Equal("Teacher", identity.Name);
    }
```

- [ ] **Step 2: Write failing login handler and endpoint tests**

Create `backend/Tests/Auth/GoogleLoginFlowTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Application.Auth;
using Application.Bookings;
using Application.DTOs;
using Domain.Users;
using Infrastructure.Google;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tests.Auth;

[Collection("Api")]
public sealed class GoogleLoginFlowTests
{
    private const string LoginRedirect = "https://lessons.giftmugweni.com/api/auth/google/login/callback";
    private readonly ApiFactory _factory;

    public GoogleLoginFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Start_uses_login_scopes_and_login_callback()
    {
        var states = new GoogleOAuthStateStore();
        var handler = new BeginGoogleLoginQueryHandler(
            Options.Create(new GoogleOAuthSettings
            {
                ClientId = "client-id",
                LoginRedirectUri = LoginRedirect,
            }),
            states);

        var url = await handler.Handle(new BeginGoogleLoginQuery(), CancellationToken.None);

        Assert.Contains("scope=openid%20email%20profile", url);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(LoginRedirect)}", url);
        Assert.DoesNotContain("calendar.events", url);
    }

    [Fact]
    public async Task Complete_rejects_invalid_state()
    {
        await using var db = NewDb();
        var handler = BuildHandler(new GoogleOAuthStateStore(), TeacherIdentity(), db);

        await Assert.ThrowsAsync<BookingException>(() =>
            handler.Handle(new CompleteGoogleLoginCommand("code", "missing"), CancellationToken.None));
    }

    [Fact]
    public async Task Complete_rejects_unverified_email()
    {
        await using var db = NewDb();
        var states = new GoogleOAuthStateStore();
        var state = states.Issue();
        var handler = BuildHandler(states, TeacherIdentity(verified: false), db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CompleteGoogleLoginCommand("code", state), CancellationToken.None));
    }

    [Fact]
    public async Task Complete_rejects_unknown_email_without_creating_user()
    {
        await using var db = NewDb();
        var states = new GoogleOAuthStateStore();
        var state = states.Issue();
        var handler = BuildHandler(states, TeacherIdentity("unknown@example.com"), db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CompleteGoogleLoginCommand("code", state), CancellationToken.None));
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Complete_uses_database_role_for_existing_user()
    {
        await using var db = NewDb();
        db.Users.Add(new User
        {
            Name = "Database Teacher",
            Email = "teacher@example.com",
            Role = UserRole.Admin,
            TimeZoneId = "Africa/Harare",
        });
        await db.SaveChangesAsync();
        var states = new GoogleOAuthStateStore();
        var state = states.Issue();
        var handler = BuildHandler(states, TeacherIdentity(), db);

        var result = await handler.Handle(
            new CompleteGoogleLoginCommand("code", state), CancellationToken.None);

        Assert.Equal("Database Teacher", result.Name);
        Assert.Equal("Admin", result.Role);
    }

    [Fact]
    public async Task Callback_sets_cookie_and_redirects_to_calendar()
    {
        using var factory = LoginFactory(TeacherIdentity());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var start = await client.GetAsync("/api/auth/google/login/start");
        var location = start.Headers.Location!.ToString();
        var state = QueryHelpers.ParseQuery(new Uri(location).Query)["state"].Single();
        var callback = await client.GetAsync(
            $"/api/auth/google/login/callback?code=code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/calendar", callback.Headers.Location?.OriginalString);
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal("Admin", me!.Role);
    }

    [Fact]
    public async Task Callback_unknown_email_redirects_without_creating_user()
    {
        using var factory = LoginFactory(TeacherIdentity("unknown@example.com"));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var start = await client.GetAsync("/api/auth/google/login/start");
        var state = QueryHelpers.ParseQuery(new Uri(start.Headers.Location!.ToString()).Query)["state"].Single();
        var callback = await client.GetAsync(
            $"/api/auth/google/login/callback?code=code&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?google=error", callback.Headers.Location?.OriginalString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.Where(u => u.Email == "unknown@example.com").ToListAsync());
    }

    private WebApplicationFactory<Program> LoginFactory(GoogleLoginIdentity identity) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Google:ClientId", "client-id");
            builder.UseSetting("Google:ClientSecret", "client-secret");
            builder.UseSetting("Google:LoginRedirectUri", LoginRedirect);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleLoginService>();
                services.AddSingleton<IGoogleLoginService>(new FakeLoginService(identity));
            });
        });

    private static CompleteGoogleLoginCommandHandler BuildHandler(
        IGoogleOAuthStateStore states,
        GoogleLoginIdentity identity,
        IAppDbContext db) =>
        new(states, new FakeLoginService(identity), db);

    private static GoogleLoginIdentity TeacherIdentity(
        string email = "teacher@example.com", bool verified = true) =>
        new("google-subject", email, verified, "Google Teacher");

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeLoginService(GoogleLoginIdentity identity) : IGoogleLoginService
    {
        public Task<GoogleLoginIdentity> AuthenticateAsync(string code, CancellationToken ct) =>
            Task.FromResult(identity);
    }

    private sealed record MeResponse(string Id, string Name, string Email, string Role);
}
```

- [ ] **Step 3: Run the new tests and verify RED**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleOAuthClientTests|FullyQualifiedName~GoogleLoginFlowTests"
```

Expected: compile/test failure because the login identity, service, handlers, routes, and user-info method do not exist.

- [ ] **Step 4: Add the login identity and service contracts**

Create `backend/Application/Abstractions/GoogleLoginIdentity.cs`:

```csharp
namespace Application.Abstractions;

public sealed record GoogleLoginIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name);
```

Create `backend/Application/Abstractions/IGoogleLoginService.cs`:

```csharp
namespace Application.Abstractions;

public interface IGoogleLoginService
{
    Task<GoogleLoginIdentity> AuthenticateAsync(string code, CancellationToken ct);
}
```

Add `LoginRedirectUri` to `GoogleOAuthSettings`:

```csharp
    public string LoginRedirectUri { get; set; } = "";
```

Create `backend/Application/Abstractions/GoogleLoginScopes.cs`:

```csharp
namespace Application.Abstractions;

public static class GoogleLoginScopes
{
    public const string OpenIdEmailProfile = "openid email profile";
}
```

- [ ] **Step 5: Implement the user-info client method**

Add the required imports to `GoogleOAuthClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Application.Abstractions;
```

Add this method and response record:

```csharp
    public async Task<GoogleLoginIdentity> GetUserInfoAsync(
        string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessWithBodyAsync(response, ct);
        var profile = await response.Content.ReadFromJsonAsync<UserInfoResponse>(ct)
            ?? throw new InvalidOperationException("Empty Google user-info response.");
        if (string.IsNullOrWhiteSpace(profile.Sub) || string.IsNullOrWhiteSpace(profile.Email))
            throw new InvalidOperationException("Google user-info response is missing identity claims.");
        return new GoogleLoginIdentity(
            profile.Sub,
            profile.Email,
            profile.EmailVerified,
            profile.Name);
    }

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("email_verified")] bool EmailVerified,
        [property: JsonPropertyName("name")] string? Name);
```

- [ ] **Step 6: Implement the login service**

Create `backend/Infrastructure/Google/GoogleLoginService.cs`:

```csharp
using Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Google;

public sealed class GoogleLoginService(
    GoogleOAuthClient oauth,
    IOptions<GoogleOAuthSettings> options) : IGoogleLoginService
{
    public async Task<GoogleLoginIdentity> AuthenticateAsync(
        string code, CancellationToken ct)
    {
        var settings = options.Value;
        var tokens = await oauth.ExchangeCodeAsync(
            code,
            settings.ClientId,
            settings.ClientSecret,
            settings.LoginRedirectUri,
            ct);
        return await oauth.GetUserInfoAsync(tokens.AccessToken, ct);
    }
}
```

Register it in `DependencyInjection.cs`:

```csharp
services.AddScoped<IGoogleLoginService, GoogleLoginService>();
```

- [ ] **Step 7: Implement the start handler**

Create `backend/Application/Auth/BeginGoogleLoginQuery.cs`:

```csharp
using Application.Abstractions;

namespace Application.Auth;

public sealed record BeginGoogleLoginQuery : IQuery<string>;
```

Create `backend/Application/Auth/BeginGoogleLoginQueryHandler.cs`:

```csharp
using Application.Abstractions;
using Application.Bookings;
using Microsoft.Extensions.Options;

namespace Application.Auth;

public sealed class BeginGoogleLoginQueryHandler(
    IOptions<GoogleOAuthSettings> options,
    IGoogleOAuthStateStore states)
    : IQueryHandler<BeginGoogleLoginQuery, string>
{
    public Task<string> Handle(BeginGoogleLoginQuery q, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) ||
            string.IsNullOrWhiteSpace(settings.LoginRedirectUri))
            throw new BookingException("Google login is not configured.");
        var state = states.Issue();
        var url = "https://accounts.google.com/o/oauth2/v2/auth?"
            + $"client_id={Uri.EscapeDataString(settings.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(settings.LoginRedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(GoogleLoginScopes.OpenIdEmailProfile)}"
            + $"&prompt=select_account&state={Uri.EscapeDataString(state)}";
        return Task.FromResult(url);
    }
}
```

- [ ] **Step 8: Implement the callback handler**

Create `backend/Application/Auth/CompleteGoogleLoginCommand.cs`:

```csharp
using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record CompleteGoogleLoginCommand(string Code, string State)
    : ICommand<UserDto>;
```

Create `backend/Application/Auth/CompleteGoogleLoginCommandHandler.cs`:

```csharp
using Application.Abstractions;
using Application.Bookings;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth;

public sealed class CompleteGoogleLoginCommandHandler(
    IGoogleOAuthStateStore states,
    IGoogleLoginService login,
    IAppDbContext db)
    : ICommandHandler<CompleteGoogleLoginCommand, UserDto>
{
    public async Task<UserDto> Handle(
        CompleteGoogleLoginCommand command, CancellationToken ct)
    {
        if (!states.Consume(command.State))
            throw new BookingException("Google login state mismatch.");
        var identity = await login.AuthenticateAsync(command.Code, ct);
        if (!identity.EmailVerified)
            throw new UnauthorizedAccessException("Google email is not verified.");
        var email = identity.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new UnauthorizedAccessException("Google account is not registered.");
        return new UserDto(user.Id, user.Name, user.Email, user.Role.ToString());
    }
}
```

- [ ] **Step 9: Share cookie issuance between login methods**

Create `backend/Endpoints/AuthCookie.cs`:

```csharp
using System.Security.Claims;
using Application.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Endpoints;

public static class AuthCookie
{
    public static Task SignInAsync(HttpContext http, UserDto user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
        ], "cookies");
        if (user.Role == "Admin")
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            });
    }
}
```

Replace the identity construction and `SignInAsync` call in `AuthEndpoints.cs` with:

```csharp
            await AuthCookie.SignInAsync(http, user);
```

- [ ] **Step 10: Add login endpoints**

Create `backend/Endpoints/GoogleLoginEndpoints.cs`:

```csharp
using Application.Auth;
using Application.Bookings;

namespace Endpoints;

public static class GoogleLoginEndpoints
{
    public static IEndpointRouteBuilder MapGoogleLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/google/login/start", async (ISender sender) =>
            Results.Redirect(await sender.Send(new BeginGoogleLoginQuery())));

        app.MapGet("/api/auth/google/login/callback", async (
            string? code,
            string? state,
            ISender sender,
            HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return Results.Redirect("/login?google=error");
            try
            {
                var user = await sender.Send(new CompleteGoogleLoginCommand(code, state));
                await AuthCookie.SignInAsync(http, user);
                return Results.Redirect("/calendar");
            }
            catch (Exception ex) when (
                ex is BookingException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
            {
                return Results.Redirect("/login?google=error");
            }
        });

        return app;
    }
}
```

Call `app.MapGoogleLogin();` in `Program.cs` after `app.MapAuth()`.

- [ ] **Step 11: Run focused and full backend tests**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleOAuthClientTests|FullyQualifiedName~GoogleLoginFlowTests|FullyQualifiedName~PersistentSessionTests"
dotnet test backend/BookingApi.slnx
```

Expected: all focused tests pass and the full backend suite has 0 failures.

- [ ] **Step 12: Commit after explicit authorization**

```bash
git add backend/Application/Abstractions/GoogleLoginIdentity.cs backend/Application/Abstractions/IGoogleLoginService.cs backend/Application/Abstractions/GoogleOAuthSettings.cs backend/Application/Abstractions/GoogleLoginScopes.cs backend/Application/Auth/BeginGoogleLoginQuery.cs backend/Application/Auth/BeginGoogleLoginQueryHandler.cs backend/Application/Auth/CompleteGoogleLoginCommand.cs backend/Application/Auth/CompleteGoogleLoginCommandHandler.cs backend/Endpoints/AuthCookie.cs backend/Endpoints/AuthEndpoints.cs backend/Endpoints/GoogleLoginEndpoints.cs backend/Host/Program.cs backend/Infrastructure/Google/GoogleOAuthClient.cs backend/Infrastructure/Google/GoogleLoginService.cs backend/Infrastructure/DependencyInjection.cs backend/Tests/Google/GoogleOAuthClientTests.cs backend/Tests/Auth/GoogleLoginFlowTests.cs
git commit -m "feat(auth): add Google login"
```

Expected: one commit containing only the listed backend and test files.

### Task 3: Add the Google login button and error UX

**Files:**
- Modify: `frontend/src/composables/useAuth.ts`
- Modify: `frontend/src/views/LoginView.vue`
- Modify: `e2e/tests/booking.spec.ts`

- [ ] **Step 1: Write the failing frontend E2E test**

Append to `e2e/tests/booking.spec.ts`:

```typescript
test('login shows Google option and preserves email fallback', async ({ page }) => {
  await page.goto('/login')

  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible()
  await expect(page.getByPlaceholder('Your email')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send code' })).toBeVisible()
})

test('login shows sanitized Google callback error', async ({ page }) => {
  await page.goto('/login?google=error')

  await expect(page.getByText('Google sign-in failed. Use the email code below or try again.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible()
})
```

- [ ] **Step 2: Run the tests and verify RED**

```bash
npx playwright test tests/booking.spec.ts --grep "login"
```

Working directory: `e2e/`

Expected: both tests fail because the Google button and callback error state do not exist.

- [ ] **Step 3: Add the auth redirect helper**

Append to `frontend/src/composables/useAuth.ts`:

```typescript
export function startGoogleLogin() {
  window.location.href = '/api/auth/google/login/start'
}
```

- [ ] **Step 4: Add the login-page state and button**

In `frontend/src/views/LoginView.vue`, import `useRoute` and `startGoogleLogin`:

```typescript
import { useRoute, useRouter } from 'vue-router'
import { requestCode, startGoogleLogin, verifyCode } from '../composables/useAuth'
```

Initialize the route-aware error:

```typescript
const route = useRoute()
const error = ref(route.query.google === 'error'
  ? 'Google sign-in failed. Use the email code below or try again.'
  : '')
```

Insert this block before the email form:

```vue
    <UButton
      v-if="!sent"
      type="button"
      block
      color="neutral"
      variant="outline"
      icon="i-lucide-chrome"
      @click="startGoogleLogin"
    >
      Continue with Google
    </UButton>

    <div v-if="!sent" class="my-4 text-center text-xs text-muted">or continue with email</div>
```

Keep the existing form, resend behavior, and email-code verification unchanged.

- [ ] **Step 5: Run frontend and E2E verification**

```bash
npm run typecheck
npm run build
npx playwright test tests/booking.spec.ts --grep "login"
```

Expected: typecheck/build pass and both login tests pass.

- [ ] **Step 6: Commit after explicit authorization**

```bash
git add frontend/src/composables/useAuth.ts frontend/src/views/LoginView.vue e2e/tests/booking.spec.ts
git commit -m "feat(auth): add Google login button"
```

Expected: one commit containing only the three listed files.

### Task 4: Wire deployment configuration and local E2E settings

**Files:**
- Modify: `infra/docker-compose.yml:17-29`
- Modify: `infra/Program.cs:73-83`
- Modify: `.github/workflows/deploy.yml:34-58`
- Modify: `e2e/playwright.config.ts:20-31`

- [ ] **Step 1: Add the login callback URI to the container**

In `infra/docker-compose.yml`, add:

```yaml
      - Google__LoginRedirectUri=${GOOGLE_LOGIN_REDIRECT_URI}
```

Keep `Google__RedirectUri` unchanged for the existing Calendar callback.

- [ ] **Step 2: Pass the deployment variable through Pulumi**

In `infra/Program.cs`, add `GOOGLE_LOGIN_REDIRECT_URI` to `optionalKeys` immediately after `GOOGLE_REDIRECT_URI`.

- [ ] **Step 3: Pass the GitHub deployment secret**

In `.github/workflows/deploy.yml`, add:

```yaml
          GOOGLE_LOGIN_REDIRECT_URI: ${{ secrets.GOOGLE_LOGIN_REDIRECT_URI }}
```

- [ ] **Step 4: Add the local E2E value**

In `e2e/playwright.config.ts`, add this backend environment entry:

```typescript
        Google__LoginRedirectUri: 'http://localhost:8080/api/auth/google/login/callback',
```

- [ ] **Step 5: Validate the configuration statically**

```bash
git diff --check
docker compose -f infra/docker-compose.yml config >/tmp/class-booking-compose-config.txt
```

Expected: no whitespace errors and Docker Compose renders the backend environment without errors.

- [ ] **Step 6: Commit after explicit authorization**

```bash
git add infra/docker-compose.yml infra/Program.cs .github/workflows/deploy.yml e2e/playwright.config.ts
git commit -m "ci(auth): configure Google login deployment"
```

Expected: one commit containing only the four listed files.

### Task 5: Run complete repository verification

**Files:**
- Verify only; no source changes expected

- [ ] **Step 1: Run backend tests**

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass with 0 failures.

- [ ] **Step 2: Run frontend tests, typecheck, and build**

```bash
npm --prefix frontend test
npm --prefix frontend run typecheck
npm --prefix frontend run build
```

Expected: all commands exit 0.

- [ ] **Step 3: Run the full Playwright suite**

```bash
npm --prefix e2e test
```

Expected: all login, Calendar, privacy, booking, and admin journeys pass.

- [ ] **Step 4: Check repository integrity**

```bash
git diff --check
git status --short --branch
```

Expected: no whitespace errors and no uncommitted implementation files.

### Task 6: Deploy and verify Google login plus session continuity

**Files:**
- No source changes

- [ ] **Step 1: Obtain explicit push permission**

Ask before running:

```bash
git push origin HEAD:main
```

Expected: explicit approval; do not force-push.

- [ ] **Step 2: Push and wait for CI**

```bash
git push origin HEAD:main
sha=$(git rev-parse HEAD)
ci_id=""
for attempt in $(seq 1 45); do
  ci_id=$(gh run list --workflow ci --commit "$sha" --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$ci_id" ]; then break; fi
  sleep 2
done
test -n "$ci_id"
gh run watch "$ci_id" --exit-status
```

Expected: CI succeeds.

- [ ] **Step 3: Set the login redirect secret and verify deployment**

Set the GitHub Actions secret without printing it:

```bash
gh secret set GOOGLE_LOGIN_REDIRECT_URI --body 'https://lessons.giftmugweni.com/api/auth/google/login/callback'
```

Trigger the manual deploy workflow, wait for it to finish, and run:

```bash
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected: health returns `{"status":"ok"}`.

- [ ] **Step 4: Add the Google Console redirect URI**

Add this exact authorized redirect URI to the existing Web application OAuth client:

```text
https://lessons.giftmugweni.com/api/auth/google/login/callback
```

Do not change the existing Calendar redirect URI.

- [ ] **Step 5: Verify first-login session behavior**

1. Sign in with the existing email-code flow.
2. Complete one Google login using the teacher account.
3. Confirm the browser lands on `/calendar` and `/api/auth/me` returns the teacher role.
4. Force a normal container replacement through the manual deploy workflow.
5. Without signing in again, reload `/admin` and confirm the session remains authenticated.

Expected: the first deployment that introduces the key ring may require one re-login; the forced replacement afterward does not log the user out.

- [ ] **Step 6: Verify existing-user restrictions**

1. Use a Google account whose verified email is not one of the configured users.
2. Confirm the app returns to `/login?google=error` and does not create an account.
3. Confirm email-code login still works for an existing student.

Expected: no unknown account is provisioned and the fallback remains available.

- [ ] **Step 7: Verify Calendar scope separation and reconnect the Calendar grant**

1. Sign in as the teacher.
2. Open `/admin` and start the existing Calendar connection.
3. Confirm the consent URL contains `calendar.events.owned` and not `calendar.events`.
4. Complete consent and confirm the admin card shows Connected.
5. Do not set `MEET_PROVIDER=google` until this owned-scope reconnect succeeds.

Expected: login consent and Calendar consent remain separate, and the previously disconnected Calendar grant is restored only through the explicit admin flow.

- [ ] **Step 8: Final hygiene checks**

```bash
gh secret list
git status --short --branch
git log --oneline -10
```

Expected: no secret values are printed, the worktree is clean, and the implementation commits are present on `origin/main`.
