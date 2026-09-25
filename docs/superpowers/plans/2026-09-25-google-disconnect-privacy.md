# Google Disconnect and Privacy Compliance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broad Google Calendar grant with `calendar.events.owned`, add a truthful admin disconnect flow, and publish an accurate privacy policy.

**Architecture:** Centralize the scope in one application constant. Extend the existing account connector and token store with best-effort remote revocation plus guaranteed active-token deletion, expose the operation through an admin command endpoint, and add a confirmation modal that reports whether Google confirmed revocation. Keep existing Calendar events untouched and preserve fixed-link fallback after disconnect.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core SQLite, xUnit, Vue 3, Nuxt UI, Playwright, GitHub Actions

---

## Execution Preconditions

- Work in `.worktrees/google-oauth-ci-rollout` on `fix/google-oauth-ci-rollout`.
- Subagents are unavailable; execute inline with `executing-plans`.
- Obtain explicit user permission before creating implementation commits and pushing `HEAD` to `origin/main`.
- Never print or log OAuth codes, access tokens, refresh tokens, encryption keys, or complete student contact data.
- Do not create a verification video; it is explicitly out of scope.
- Preserve existing Google Calendar events and Meet links during disconnect.
- After the plan is approved, obtain explicit permission before committing the plan/spec clarification; Task 5 and Task 6 assume those documentation files are committed.

## File Map

| File | Responsibility |
|---|---|
| `backend/Application/Abstractions/GoogleOAuthScopes.cs` | Own the single runtime Calendar scope |
| `backend/Application/Auth/BeginGoogleOAuthQueryHandler.cs` | Request the owned-calendar scope |
| `backend/Infrastructure/Google/EfGoogleTokenStore.cs` | Persist the owned scope and delete active credentials |
| `backend/Application/Abstractions/IGoogleTokenStore.cs` | Expose credential deletion |
| `backend/Application/Abstractions/IGoogleAccountConnector.cs` | Expose disconnect orchestration |
| `backend/Infrastructure/Google/GoogleAccountConnector.cs` | Revoke remotely, report the result, and always delete locally |
| `backend/Application/Auth/DisconnectGoogleCommand.cs` | Request disconnect |
| `backend/Application/DTOs/GoogleDisconnectDto.cs` | Return whether Google confirmed revocation |
| `backend/Application/Auth/DisconnectGoogleCommandHandler.cs` | Map connector result to the command result |
| `backend/Endpoints/GoogleAuthEndpoints.cs` | Expose admin-only `DELETE /api/admin/google` |
| `frontend/src/views/AdminView.vue` | Show connection details and confirmation modal |
| `frontend/src/views/PrivacyView.vue` | Publish complete user-facing disclosures |
| `backend/Tests/Auth/GoogleAuthFlowTests.cs` | Verify the exact OAuth scope and endpoint authorization |
| `backend/Tests/Google/GoogleAccountConnectorTests.cs` | Verify remote/local disconnect behavior |
| `backend/Tests/Google/GoogleCalendarProviderTests.cs` | Update the existing token-store fake for the deletion contract |
| `backend/Tests/Google/GoogleTokenStoreTests.cs` | Verify scope persistence and deletion |
| `e2e/tests/booking.spec.ts` | Verify admin disconnect states and privacy disclosures |

### Task 0: Commit the approved planning documents

- [ ] **Step 1: Obtain explicit authorization**

Ask before committing the approved plan and the spec clarification.

- [ ] **Step 2: Commit documentation only**

```bash
git add docs/superpowers/specs/2026-09-25-google-disconnect-privacy-design.md docs/superpowers/plans/2026-09-25-google-disconnect-privacy.md
git commit -m "docs: add Google disconnect implementation plan"
```

Expected: the commit contains only the two planning documents.

### Task 1: Narrow the runtime OAuth scope

**Files:**
- Create: `backend/Application/Abstractions/GoogleOAuthScopes.cs`
- Modify: `backend/Application/Auth/BeginGoogleOAuthQueryHandler.cs:17-23`
- Modify: `backend/Infrastructure/Google/EfGoogleTokenStore.cs:8-33`
- Modify: `backend/Tests/Auth/GoogleAuthFlowTests.cs:35-40`
- Create: `backend/Tests/Google/GoogleTokenStoreTests.cs`

- [ ] **Step 1: Write failing exact-scope tests**

In `backend/Tests/Auth/GoogleAuthFlowTests.cs`, replace the scope assertion in `Start_redirects_to_google_for_admin` with:

```csharp
        const string ownedScope = "https://www.googleapis.com/auth/calendar.events.owned";
        Assert.Contains($"scope={Uri.EscapeDataString(ownedScope)}&access_type=offline", location);
        Assert.DoesNotContain(
            $"scope={Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events")}&access_type=offline",
            location);
```

Create `backend/Tests/Google/GoogleTokenStoreTests.cs` with:

```csharp
using Application.Abstractions;
using Domain.Auth;
using Infrastructure.Google;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tests.Google;

public sealed class GoogleTokenStoreTests
{
    [Fact]
    public async Task Save_persists_owned_calendar_scope()
    {
        await using var db = NewDb();
        var store = new EfGoogleTokenStore(db);

        await store.SaveAsync(
            new GoogleTokenData("encrypted", "access", DateTime.UtcNow.AddHours(1), false),
            Guid.NewGuid(),
            CancellationToken.None);

        var row = await db.GoogleTokens.SingleAsync();
        Assert.Equal("https://www.googleapis.com/auth/calendar.events.owned", row.Scope);
    }

    [Fact]
    public async Task Get_marks_existing_broad_token_as_needing_reconnect()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.GoogleTokens.Add(new GoogleToken
        {
            UserId = userId,
            RefreshTokenEncrypted = "encrypted",
            AccessToken = "access",
            ExpiryUtc = DateTime.UtcNow.AddHours(1),
            Scope = "https://www.googleapis.com/auth/calendar.events",
        });
        await db.SaveChangesAsync();
        var store = new EfGoogleTokenStore(db);

        var token = await store.GetAsync(CancellationToken.None);

        Assert.NotNull(token);
        Assert.True(token.NeedsReconnect);
    }

    [Fact]
    public async Task Save_updates_existing_token_scope()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.GoogleTokens.Add(new GoogleToken
        {
            UserId = userId,
            RefreshTokenEncrypted = "old-encrypted",
            AccessToken = "old-access",
            ExpiryUtc = DateTime.UtcNow,
            Scope = "https://www.googleapis.com/auth/calendar.events",
        });
        await db.SaveChangesAsync();
        var store = new EfGoogleTokenStore(db);

        await store.SaveAsync(
            new GoogleTokenData("new-encrypted", "new-access", DateTime.UtcNow.AddHours(1), false),
            userId,
            CancellationToken.None);

        Assert.Equal(
            "https://www.googleapis.com/auth/calendar.events.owned",
            (await db.GoogleTokens.SingleAsync()).Scope);
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleAuthFlowTests.Start_redirects_to_google_for_admin|FullyQualifiedName~GoogleTokenStoreTests"
```

Expected: all 4 tests fail because the runtime scope and existing-scope handling still use `calendar.events`.

- [ ] **Step 3: Add the shared scope constant**

Create `backend/Application/Abstractions/GoogleOAuthScopes.cs`:

```csharp
namespace Application.Abstractions;

public static class GoogleOAuthScopes
{
    public const string CalendarEventsOwned =
        "https://www.googleapis.com/auth/calendar.events.owned";
}
```

- [ ] **Step 4: Use the constant in authorization and persistence**

In `backend/Application/Auth/BeginGoogleOAuthQueryHandler.cs`, replace the scope interpolation with:

```csharp
            + $"&scope={Uri.EscapeDataString(GoogleOAuthScopes.CalendarEventsOwned)}"
```

In `backend/Infrastructure/Google/EfGoogleTokenStore.cs`, remove the private `CalendarScope` field. In `GetAsync`, mark a legacy broad-scope row as needing reconnect:

```csharp
            : new GoogleTokenData(
                row.RefreshTokenEncrypted,
                row.AccessToken ?? "",
                row.ExpiryUtc,
                row.NeedsReconnect || row.Scope != GoogleOAuthScopes.CalendarEventsOwned);
```

Use the constant when creating a row:

```csharp
                Scope = GoogleOAuthScopes.CalendarEventsOwned,
```

In both the normal update path and the `DbUpdateException` retry path, set:

```csharp
            row.Scope = GoogleOAuthScopes.CalendarEventsOwned;
```

- [ ] **Step 5: Run focused and full backend tests**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleAuthFlowTests|FullyQualifiedName~GoogleTokenStoreTests"
dotnet test backend/BookingApi.slnx
```

Expected: all focused tests pass; the full suite passes with 0 failures.

- [ ] **Step 6: Commit after explicit authorization**

```bash
git add backend/Application/Abstractions/GoogleOAuthScopes.cs backend/Application/Auth/BeginGoogleOAuthQueryHandler.cs backend/Infrastructure/Google/EfGoogleTokenStore.cs backend/Tests/Auth/GoogleAuthFlowTests.cs backend/Tests/Google/GoogleTokenStoreTests.cs
git commit -m "fix(auth): narrow Google Calendar scope"
```

Expected: one commit containing only the five listed files.

### Task 2: Implement backend disconnect and token deletion

**Files:**
- Modify: `backend/Application/Abstractions/IGoogleTokenStore.cs`
- Modify: `backend/Application/Abstractions/IGoogleAccountConnector.cs`
- Modify: `backend/Infrastructure/Google/EfGoogleTokenStore.cs`
- Modify: `backend/Infrastructure/Google/GoogleAccountConnector.cs`
- Create: `backend/Application/Auth/DisconnectGoogleCommand.cs`
- Create: `backend/Application/DTOs/GoogleDisconnectDto.cs`
- Create: `backend/Application/Auth/DisconnectGoogleCommandHandler.cs`
- Modify: `backend/Endpoints/GoogleAuthEndpoints.cs`
- Create: `backend/Tests/Google/GoogleAccountConnectorTests.cs`
- Modify: `backend/Tests/Google/GoogleCalendarProviderTests.cs:30-49`
- Modify: `backend/Tests/Auth/GoogleAuthFlowTests.cs`
- Modify: `backend/Tests/Google/GoogleTokenStoreTests.cs`

- [ ] **Step 1: Write failing connector tests**

Create `backend/Tests/Google/GoogleAccountConnectorTests.cs`:

```csharp
using System.Net;
using Application.Abstractions;
using Infrastructure.Google;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Google;

public sealed class GoogleAccountConnectorTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public async Task Disconnect_revokes_refresh_token_and_deletes_local_token()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        string? body = null;
        var connector = Build(crypto, store, request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.True(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
        Assert.Contains("token=1//refresh", Uri.UnescapeDataString(body!));
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_remote_revocation_fails()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        var connector = Build(crypto, store, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_token_cannot_be_decrypted()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(new GoogleTokenData(
            "not-valid-ciphertext", "access", DateTime.UtcNow.AddHours(1), false));
        var connector = Build(crypto, store, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_deletes_local_token_when_request_is_cancelled()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(Token(crypto, "1//refresh"));
        var connector = Build(crypto, store, _ => throw new OperationCanceledException());

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(store.Deleted);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Disconnect_without_token_is_idempotent()
    {
        var crypto = new GoogleTokenCrypto(Key);
        var store = new FakeStore(null);
        var connector = Build(crypto, store, _ => throw new InvalidOperationException("HTTP must not be called"));

        var result = await connector.DisconnectAsync(CancellationToken.None);

        Assert.True(result);
        Assert.True(store.Deleted);
    }

    private static GoogleTokenData Token(GoogleTokenCrypto crypto, string refreshToken) =>
        new(crypto.Encrypt(refreshToken), "access", DateTime.UtcNow.AddHours(1), false);

    private static GoogleAccountConnector Build(
        GoogleTokenCrypto crypto,
        FakeStore store,
        Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(
            new GoogleOAuthClient(new HttpClient()),
            Options.Create(new GoogleOAuthSettings()),
            crypto,
            store,
            new StubHttpClientFactory(new HttpClient(new StubHandler(response))),
            NullLogger<GoogleAccountConnector>.Instance);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response(request));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeStore(GoogleTokenData? current) : IGoogleTokenStore
    {
        public GoogleTokenData? Current { get; private set; } = current;
        public bool Deleted { get; private set; }

        public Task<GoogleTokenData?> GetAsync(CancellationToken ct) =>
            Task.FromResult(Current);

        public Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FlagReconnectAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken ct)
        {
            Current = null;
            Deleted = true;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Extend store tests for deletion**

Add to `backend/Tests/Google/GoogleTokenStoreTests.cs`:

```csharp
    [Fact]
    public async Task Delete_removes_active_google_token()
    {
        await using var db = NewDb();
        var store = new EfGoogleTokenStore(db);
        await store.SaveAsync(
            new GoogleTokenData("encrypted", "access", DateTime.UtcNow.AddHours(1), false),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Single(await db.GoogleTokens.ToListAsync());

        await store.DeleteAsync(CancellationToken.None);

        Assert.Empty(await db.GoogleTokens.ToListAsync());
    }
```

- [ ] **Step 3: Add failing endpoint authorization tests**

Add to `backend/Tests/Auth/GoogleAuthFlowTests.cs`:

```csharp
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
        var client = _factory.CreateClient();
        await LoginAsTeacherAsync(client);

        var res = await client.DeleteAsync("/api/admin/google");
        var body = await res.Content.ReadFromJsonAsync<GoogleDisconnectResponse>();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.RemoteRevoked);
    }

    private sealed record GoogleDisconnectResponse(bool RemoteRevoked);
```

- [ ] **Step 4: Run the new tests and verify RED**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleAccountConnectorTests|FullyQualifiedName~GoogleTokenStoreTests.Delete|FullyQualifiedName~GoogleAuthFlowTests.Disconnect"
```

Expected: compile/test failure because disconnect contracts, command, and endpoint do not exist.

- [ ] **Step 5: Extend the application contracts**

Replace `backend/Application/Abstractions/IGoogleTokenStore.cs` with:

```csharp
namespace Application.Abstractions;

public sealed record GoogleTokenData(string RefreshTokenEncrypted, string AccessToken, DateTime ExpiryUtc, bool NeedsReconnect);

public interface IGoogleTokenStore
{
    Task<GoogleTokenData?> GetAsync(CancellationToken ct);
    Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct);
    Task FlagReconnectAsync(CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
}
```

Replace `backend/Application/Abstractions/IGoogleAccountConnector.cs` with:

```csharp
namespace Application.Abstractions;

public interface IGoogleAccountConnector
{
    Task ConnectAsync(string code, Guid userId, CancellationToken ct);
    Task<bool> DisconnectAsync(CancellationToken ct);
}
```

Add this method to `EfGoogleTokenStore`:

```csharp
    public async Task DeleteAsync(CancellationToken ct)
    {
        var rows = await db.GoogleTokens.ToListAsync(ct);
        db.GoogleTokens.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }
```

Add this method to the existing `FakeStore` in `backend/Tests/Google/GoogleCalendarProviderTests.cs`:

```csharp
        public Task DeleteAsync(CancellationToken ct)
        {
            Current = null;
            return Task.CompletedTask;
        }
```

- [ ] **Step 6: Implement connector disconnect**

Add this method to `GoogleAccountConnector` without adding comments:

```csharp
    public async Task<bool> DisconnectAsync(CancellationToken ct)
    {
        var remoteRevoked = false;
        try
        {
            var existing = await store.GetAsync(ct);
            if (existing is null) return true;

            var refreshToken = crypto.Decrypt(existing.RefreshTokenEncrypted);
            if (string.IsNullOrEmpty(refreshToken))
                throw new CryptographicException("Stored Google refresh token is empty.");

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
            });
            using var http = httpFactory.CreateClient();
            using var response = await http.PostAsync(
                "https://oauth2.googleapis.com/revoke", content, ct);
            remoteRevoked = response.IsSuccessStatusCode;
            if (!remoteRevoked)
                log.LogWarning(
                    "Google token revocation returned {StatusCode}; deleting the local token.",
                    (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google token revocation failed; deleting the local token.");
        }
        finally
        {
            await store.DeleteAsync(CancellationToken.None);
        }

        return remoteRevoked;
    }
```

- [ ] **Step 7: Add command, DTO, handler, and endpoint**

Create `backend/Application/Auth/DisconnectGoogleCommand.cs`:

```csharp
using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record DisconnectGoogleCommand : ICommand<GoogleDisconnectDto>;
```

Create `backend/Application/DTOs/GoogleDisconnectDto.cs`:

```csharp
namespace Application.DTOs;

public sealed record GoogleDisconnectDto(bool RemoteRevoked);
```

Create `backend/Application/Auth/DisconnectGoogleCommandHandler.cs`:

```csharp
using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed class DisconnectGoogleCommandHandler(IGoogleAccountConnector connector)
    : ICommandHandler<DisconnectGoogleCommand, GoogleDisconnectDto>
{
    public async Task<GoogleDisconnectDto> Handle(
        DisconnectGoogleCommand command, CancellationToken ct) =>
        new(await connector.DisconnectAsync(ct));
}
```

Add this endpoint to `GoogleAuthEndpoints.MapGoogleAuth`:

```csharp
        app.MapDelete("/api/admin/google", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new DisconnectGoogleCommand(), ct)))
           .RequireAuthorization(p => p.RequireRole("Admin"));
```

- [ ] **Step 8: Run focused and full backend tests**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~GoogleAccountConnectorTests|FullyQualifiedName~GoogleTokenStoreTests|FullyQualifiedName~GoogleAuthFlowTests"
dotnet test backend/BookingApi.slnx
```

Expected: all focused tests pass; full backend suite passes with 0 failures.

- [ ] **Step 9: Commit after explicit authorization**

```bash
git add backend/Application/Abstractions/IGoogleTokenStore.cs backend/Application/Abstractions/IGoogleAccountConnector.cs backend/Infrastructure/Google/EfGoogleTokenStore.cs backend/Infrastructure/Google/GoogleAccountConnector.cs backend/Application/Auth/DisconnectGoogleCommand.cs backend/Application/DTOs/GoogleDisconnectDto.cs backend/Application/Auth/DisconnectGoogleCommandHandler.cs backend/Endpoints/GoogleAuthEndpoints.cs backend/Tests/Google/GoogleAccountConnectorTests.cs backend/Tests/Google/GoogleCalendarProviderTests.cs backend/Tests/Google/GoogleTokenStoreTests.cs backend/Tests/Auth/GoogleAuthFlowTests.cs
git commit -m "feat(auth): add Google disconnect"
```

Expected: one commit containing only the listed files.

### Task 3: Add the admin disconnect experience

**Files:**
- Modify: `frontend/src/views/AdminView.vue:20-37`
- Modify: `frontend/src/views/AdminView.vue:139-151`
- Modify: `frontend/src/views/AdminView.vue:210-229`
- Modify: `e2e/tests/booking.spec.ts`

- [ ] **Step 1: Write failing Playwright disconnect tests**

Append to `e2e/tests/booking.spec.ts`:

```typescript
test('admin disconnects Google and returns to Connect state', async ({ page }) => {
  await login(page, 'teacher@example.com')
  let connected = true
  let deleteRequests = 0

  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', async route => {
    if (route.request().method() !== 'DELETE') return route.fallback()
    deleteRequests += 1
    connected = false
    await route.fulfill({ json: { remoteRevoked: true } })
  })

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await expect(page.getByText('calendar.events.owned')).toBeVisible()
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await expect(page.getByText('Disconnect Google Calendar?')).toBeVisible()
  await page.getByRole('button', { name: 'Cancel' }).click()
  expect(deleteRequests).toBe(0)

  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await page.getByRole('button', { name: 'Disconnect Google' }).last().click()

  await expect(page.getByText('Google disconnected. New bookings use the fallback link.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Connect Google' })).toBeVisible()
  expect(deleteRequests).toBe(1)
})

test('admin disconnect warns when local access ends before Google confirms revocation', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected: true, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', route => route.fulfill({
    json: { remoteRevoked: false },
  }))

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await page.getByRole('button', { name: 'Disconnect Google' }).last().click()

  await expect(page.getByText(/Google did not confirm revocation/)).toBeVisible()
  await expect(page.getByText(/Google Account Settings/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Connect Google' })).toBeVisible()
})

test('admin keeps connected state when local disconnect fails', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected: true, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', route => route.fulfill({
    status: 500,
    json: { error: 'Could not disconnect Google.' },
  }))

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await page.getByRole('button', { name: 'Disconnect Google' }).last().click()
  await expect(page.getByText('Disconnect Google Calendar?')).toBeVisible()
  await page.getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByText('Could not disconnect Google.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Disconnect Google' })).toBeVisible()
  await expect(page.getByText('Connected')).toBeVisible()
})

test('admin disconnects reconnect-required Google token', async ({ page }) => {
  await login(page, 'teacher@example.com')
  let needsReconnect = true

  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected: false, needsReconnect },
  }))
  await page.route('**/api/admin/google', async route => {
    if (route.request().method() !== 'DELETE') return route.fallback()
    needsReconnect = false
    await route.fulfill({ json: { remoteRevoked: true } })
  })

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await expect(page.getByText('Reconnect required').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reconnect Google' })).toBeVisible()
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await page.getByRole('button', { name: 'Disconnect Google' }).last().click()

  await expect(page.getByRole('button', { name: 'Connect Google' })).toBeVisible()
  expect(needsReconnect).toBe(false)
})
```

- [ ] **Step 2: Run the tests and verify RED**

```bash
npx playwright test tests/booking.spec.ts --grep "disconnect"
```

Working directory: `e2e/`

Expected: all 4 tests fail because Disconnect UI and endpoint behavior are absent.

- [ ] **Step 3: Add disconnect state and action to `AdminView.vue`**

Add these refs after `googleNoticeError`:

```typescript
const googleDisconnectOpen = ref(false)
const googleDisconnecting = ref(false)
const googleNoticeWarning = ref(false)
```

Add this function after `connectGoogle`:

```typescript
async function disconnectGoogle() {
  googleDisconnecting.value = true
  googleNotice.value = ''
  googleNoticeError.value = false
  googleNoticeWarning.value = false
  try {
    const result = await api<{ remoteRevoked: boolean }>('/admin/google', { method: 'DELETE' })
    googleDisconnectOpen.value = false
    googleConnected.value = false
    googleNeedsReconnect.value = false
    googleNotice.value = result.remoteRevoked
      ? 'Google disconnected. New bookings use the fallback link.'
      : 'Local Google access was removed, but Google did not confirm revocation. Remove Lesson Booking from Google Account Settings → Security → Third-party connections.'
    googleNoticeError.value = false
    googleNoticeWarning.value = !result.remoteRevoked
    await loadGoogleStatus()
  } catch (e: unknown) {
    googleNotice.value = e instanceof Error ? e.message : 'Could not disconnect Google.'
    googleNoticeError.value = true
    googleNoticeWarning.value = false
  } finally {
    googleDisconnecting.value = false
  }
}
```

- [ ] **Step 4: Replace the connected-state template block**

Replace the current reconnect alert and Connect-only container with:

```vue
    <UAlert
      v-if="!googleLoading && googleNeedsReconnect"
      color="error" variant="subtle" icon="i-lucide-circle-alert"
      title="Reconnect Google"
      description="Your stored Google permission needs attention. Reconnect to request the narrower calendar.events.owned permission, or disconnect to remove local access."
      class="mb-4"
    />

    <UCard
      v-if="!googleLoading && (googleConnected || googleNeedsReconnect)"
      variant="outline"
      class="mb-4"
    >
      <template #header>
        <div class="flex items-center justify-between gap-4">
          <h2 class="font-semibold text-highlighted">Google Calendar</h2>
          <UBadge
            :color="googleNeedsReconnect ? 'warning' : 'success'"
            variant="soft"
          >
            {{ googleNeedsReconnect ? 'Reconnect required' : 'Connected' }}
          </UBadge>
        </div>
      </template>

      <p class="text-muted">
        {{ googleNeedsReconnect
          ? 'Reconnect for new Meet links, or disconnect to remove local Google access.'
          : 'Create and remove lesson events with Meet links on your primary calendar.' }}
      </p>
      <p class="mt-3 text-sm text-muted"><strong>Required permission:</strong> calendar.events.owned</p>

      <template #footer>
        <div class="flex flex-wrap items-center justify-between gap-3">
          <ULink to="/privacy" class="text-sm text-primary">Privacy policy</ULink>
          <div class="flex flex-wrap gap-2">
            <UButton
              v-if="googleNeedsReconnect"
              color="primary"
              icon="i-lucide-refresh-cw"
              @click="connectGoogle"
            >
              Reconnect Google
            </UButton>
            <UButton
              color="error"
              variant="soft"
              icon="i-lucide-unlink"
              @click="googleDisconnectOpen = true"
            >
              Disconnect Google
            </UButton>
          </div>
        </div>
      </template>
    </UCard>

    <div v-if="!googleLoading && !googleConnected && !googleNeedsReconnect" class="mb-4">
      <UButton color="primary" icon="i-lucide-calendar-plus" @click="connectGoogle">
        Connect Google
      </UButton>
    </div>
```

Change the existing Google notice color binding from `googleNoticeError ? 'error' : 'success'` to:

```vue
      :color="googleNoticeError ? 'error' : googleNoticeWarning ? 'warning' : 'success'"
      :icon="googleNoticeError ? 'i-lucide-circle-alert' : googleNoticeWarning ? 'i-lucide-triangle-alert' : 'i-lucide-check'"
```

- [ ] **Step 5: Add the confirmation modal**

Insert before the existing backup modal:

```vue
    <UModal :open="googleDisconnectOpen" @update:open="googleDisconnectOpen = $event">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">Disconnect Google Calendar?</h2>
          </template>

          <p class="text-muted">Lesson Booking will lose permission to create or remove Calendar events.</p>
          <UAlert
            color="warning"
            variant="subtle"
            icon="i-lucide-info"
            title="Existing events are preserved"
            description="Your Google Calendar events and Meet links stay in your Google account. New bookings use the configured fallback link until you reconnect."
            class="mt-4"
          />

          <template #footer>
            <div class="flex justify-end gap-2">
              <UButton color="neutral" variant="soft" @click="googleDisconnectOpen = false">Cancel</UButton>
              <UButton
                color="error"
                icon="i-lucide-unlink"
                :loading="googleDisconnecting"
                @click="disconnectGoogle"
              >
                Disconnect Google
              </UButton>
            </div>
          </template>
        </UCard>
      </template>
    </UModal>
```

- [ ] **Step 6: Run frontend and focused E2E verification**

```bash
npm --prefix frontend run typecheck
npm --prefix frontend run build
npm --prefix e2e test -- --grep "disconnect"
```

Expected: typecheck/build pass; 4 disconnect tests pass.

- [ ] **Step 7: Commit after explicit authorization**

```bash
git add frontend/src/views/AdminView.vue e2e/tests/booking.spec.ts
git commit -m "feat(auth): add Google disconnect controls"
```

Expected: one commit containing only the two listed files.

### Task 4: Publish the complete privacy policy

**Files:**
- Modify: `frontend/src/views/PrivacyView.vue`
- Modify: `e2e/tests/booking.spec.ts`

- [ ] **Step 1: Write the failing privacy disclosure test**

Append to `e2e/tests/booking.spec.ts`:

```typescript
test('privacy policy discloses Google data and user controls', async ({ page }) => {
  await page.goto('/privacy')

  await expect(page.getByText('Last updated: 25 September 2026')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Google Calendar' })).toBeVisible()
  await expect(page.getByText(/calendar.events.owned/)).toBeVisible()
  await expect(page.getByText(/AES-256-GCM/)).toBeVisible()
  await expect(page.getByText(/revoked-token record/)).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Disconnect and deletion' })).toBeVisible()
  await expect(page.getByText(/Google Account Settings/)).toBeVisible()
})
```

- [ ] **Step 2: Run the test and verify RED**

Working directory: `e2e/`

```bash
npx playwright test tests/booking.spec.ts --grep "privacy policy"
```

Expected: failure because the current policy has only three generic sections.

- [ ] **Step 3: Replace `PrivacyView.vue` with the approved disclosures**

```vue
<script setup lang="ts">
const sections = [
  {
    title: 'Information we collect',
    body: 'We collect your email address, optional phone or WhatsApp number, short-lived login codes, and lesson bookings. When the teacher connects Google Calendar, we also store an encrypted Google refresh token, the Google event identifier, event time, and Meet link for lesson events.',
  },
  {
    title: 'Google Calendar',
    body: 'Only the teacher can connect Google Calendar. The app requests the calendar.events.owned permission to create and remove lesson events on calendars the teacher owns. It does not read existing event content. The booked student receives the event invitation and any cancellation notice.',
  },
  {
    title: 'How information is used and shared',
    body: 'Information is used to run lessons, authenticate users, display the shared calendar, send login codes and reminders, and create or remove Google Calendar events. We do not use Google data for advertising, profiling, sale, or AI training.',
  },
  {
    title: 'Service providers',
    body: 'The application runs on DigitalOcean. Database backups are stored in Cloudflare R2. Email is delivered through Resend, WhatsApp through Twilio when configured, and Calendar data through Google. These providers process information to provide their services under their own privacy terms.',
  },
  {
    title: 'Security and retention',
    body: 'The site uses HTTPS. Google refresh tokens are encrypted with AES-256-GCM, and the encryption key is stored separately from the booking database. Login codes expire quickly. Booking information remains while the service is operated. Hosting and security systems may process IP address, request time, route, and user-agent information.',
  },
  {
    title: 'Disconnect and deletion',
    body: 'Disconnecting from Admin asks Google to revoke access and always removes the active local token. If Google does not confirm revocation, the app explains how to remove it from Google Account Settings. Existing Calendar events remain in the teacher’s Google account. Historical Cloudflare R2 database backups are not purged immediately, so an old backup may retain an already-encrypted revoked-token record; it cannot authorize Google access. Request account or data deletion by email.',
  },
]
</script>

<template>
  <div class="space-y-4">
    <div>
      <h1 class="text-2xl font-bold text-highlighted">Privacy Policy</h1>
      <p class="mt-1 text-sm text-muted">Last updated: 25 September 2026</p>
    </div>

    <UCard v-for="s in sections" :key="s.title" variant="outline">
      <template #header>
        <h2 class="font-semibold text-highlighted">{{ s.title }}</h2>
      </template>
      <p class="text-muted">{{ s.body }}</p>
    </UCard>

    <UCard variant="subtle">
      <p class="text-muted">Questions or deletion requests? Contact <ULink to="mailto:giftmugweni@gmail.com" class="text-primary">giftmugweni@gmail.com</ULink></p>
    </UCard>
  </div>
</template>
```

- [ ] **Step 4: Run policy and full E2E verification**

```bash
npm --prefix e2e test -- --grep "privacy policy"
npm --prefix e2e test
```

Expected: policy test passes; all Playwright journeys pass.

- [ ] **Step 5: Commit after explicit authorization**

```bash
git add frontend/src/views/PrivacyView.vue e2e/tests/booking.spec.ts
git commit -m "docs: publish Google data privacy disclosures"
```

Expected: one commit containing only the two listed files.

### Task 5: Run complete repository verification

**Files:**
- Verify only; no source changes expected

- [ ] **Step 1: Run backend tests**

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass, 0 failures.

- [ ] **Step 2: Run frontend unit tests**

```bash
npm --prefix frontend test
```

Expected: all Vitest tests pass.

- [ ] **Step 3: Run typecheck and production build**

```bash
npm --prefix frontend run typecheck
npm --prefix frontend run build
```

Expected: both exit 0.

- [ ] **Step 4: Run full Playwright suite**

```bash
npm --prefix e2e test
```

Expected: all tests pass.

- [ ] **Step 5: Check patch integrity and repository state**

```bash
git diff --check
git status --short --branch
```

Expected: no whitespace errors and no uncommitted implementation files.

### Task 6: Publish and migrate the production grant

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

- [ ] **Step 3: Wait for automatic deployment and health**

Run:

```bash
deploy_id=""
for attempt in $(seq 1 60); do
  deploy_id=$(gh run list --workflow deploy --commit "$sha" --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$deploy_id" ]; then break; fi
  sleep 2
done
test -n "$deploy_id"
gh run watch "$deploy_id" --exit-status
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected: deploy succeeds; health returns `{"status":"ok"}`.

- [ ] **Step 4: Revoke and delete the existing broad grant**

In the production admin page:

1. Sign in as teacher if the deploy invalidated the session.
2. Open `https://lessons.giftmugweni.com/admin`.
3. Click **Disconnect Google** and confirm.
4. Require the success message confirming Google revocation.

Expected: the card disappears, Connect returns, and `/api/admin/google/status` becomes:

```json
{"connected":false,"needsReconnect":false}
```

If the UI reports that Google did not confirm revocation, stop the migration and remove Lesson Booking from Google Account Settings before reconnecting.

- [ ] **Step 5: Reconnect with the owned scope**

1. Click **Connect Google**.
2. Complete Google sign-in and consent in a normal browser.
3. Confirm the consent screen names only `calendar.events.owned`.
4. Return to `/admin` and require the connected state.

Expected: no reconnect warning and no broad scope in the consent URL.

- [ ] **Step 6: Deploy the Google provider after confirmed reconnect**

After the owned-scope reconnect succeeds, set the provider explicitly:

```bash
gh secret set MEET_PROVIDER --body google
```

Trigger and watch a manual deploy using the repaired `deploy.yml` workflow, then verify health.

- [ ] **Step 7: Run reversible booking verification**

Use a far-future unused date, create one booking, confirm a Calendar event with a Meet link, cancel the booking, and confirm Google removes the event and the app releases the slot.

Expected: event create/delete succeeds; the date returns to a reusable bookable state.

- [ ] **Step 8: Final hygiene checks**

```bash
gh secret list
git status --short --branch
git log --oneline -8
```

Expected: no secrets are printed, the worktree is clean, and implementation commits are present locally and on `origin/main`.
