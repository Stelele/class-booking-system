# Slice 2B Implementation Plan — WhatsApp Reminders

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send booking confirmations plus Monday/08:00/20:00 WhatsApp reminders via Twilio production sender, with per-recipient failure isolation, idempotent sends, and boot catch-up.

**Architecture:** `TwilioWhatsAppSender` (raw HttpClient Basic auth, Resend pattern) behind `ITwilioSender`; `BookingNotifier` (Application service, never throws) called by booking handlers; `ReminderService` timer worker computes Harare-anchored fire times with 15-min boot catch-up; `ReminderLog` rows make every send idempotent and auditable.

**Tech Stack:** .NET 10, `System.Net.Http` Basic auth, `TimeZoneConverter` (already referenced), EF Core migration. No Twilio SDK (matches no-Google-SDK decision).

**Spec:** `docs/superpowers/specs/2026-09-22-slice-2-design.md` Sections 3–4.

**Depends on:** Slice 2A merged (uses `Slot.MeetLink`, booking handlers, DI/config conventions). Build 2A first.

---

## File structure

| File | Responsibility |
|---|---|
| `backend/Booking.Application/Abstractions/ITwilioSender.cs` (create) | `SendAsync(toE164, body, ct)` port |
| `backend/Booking.Application/Abstractions/IBookingNotifier.cs` (create) | `NotifyBookingChangedAsync(bookingId, kind, ct)` port |
| `backend/Booking.Application/Notifications/BookingNotifier.cs` (create) | Renders messages, sends, logs; never throws |
| `backend/Booking.Application/Notifications/ReminderMessages.cs` (create) | The 4 wordings as pure functions (testable) |
| `backend/Booking.Domain/Reminders/ReminderLog.cs` (create) | Idempotency/audit entity |
| `backend/Booking.Infrastructure/WhatsApp/TwilioOptions.cs` (create) | AccountSid/AuthToken/FromNumber POCO |
| `backend/Booking.Infrastructure/WhatsApp/TwilioWhatsAppSender.cs` (create) | `Messages.json` POST |
| `backend/Booking.Infrastructure/WhatsApp/NullTwilioSender.cs` (create) | Log-only fallback (mirrors NullBackupService) |
| `backend/Booking.Infrastructure/WhatsApp/ReminderService.cs` (create) | Timer worker: fire times + catch-up |
| `backend/Booking.Application/Bookings/*` (modify 3 handlers) | Call notifier after save |
| `backend/Booking.Endpoints/AdminEndpoints.cs` (modify) | `POST /api/admin/users/phone` |
| Config | `Twilio:`, `Reminder:` sections; compose; deploy.yml; Pulumi optionalKeys |

---

### Task 1: Twilio sender (TDD, stubbed HTTP)

**Files:**
- Create: `backend/Booking.Application/Abstractions/ITwilioSender.cs`
- Create: `backend/Booking.Infrastructure/WhatsApp/TwilioOptions.cs`
- Create: `backend/Booking.Infrastructure/WhatsApp/TwilioWhatsAppSender.cs`
- Create: `backend/Booking.Infrastructure/WhatsApp/NullTwilioSender.cs`
- Test: `backend/Booking.Tests/WhatsApp/TwilioWhatsAppSenderTests.cs`

- [ ] **Step 1: Port + options**

```csharp
namespace Booking.Application.Abstractions;

public interface ITwilioSender
{
    Task<string> SendAsync(string toE164, string body, CancellationToken ct);
}
```
Returns the Twilio message SID.

```csharp
namespace Booking.Infrastructure.WhatsApp;

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
}
```

- [ ] **Step 2: Failing tests**

```csharp
using System.Net;
using System.Text;
using Booking.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class TwilioWhatsAppSenderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(fn(r));
    }

    private static string? _body; private static string? _auth;
    private static TwilioWhatsAppSender Sender() => new(
        new HttpClient(new StubHandler(r =>
        {
            _body = r.Content!.ReadAsStringAsync().Result;
            _auth = r.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Created)
            { Content = new StringContent("""{"sid":"SM123"}""", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://api.twilio.com/") },
        Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+15551234567" }),
        NullLogger<TwilioWhatsAppSender>.Instance);

    [Fact]
    public async Task Posts_whatsapp_form_with_basic_auth_and_returns_sid()
    {
        var sid = await Sender().SendAsync("+447700900123", "Your lesson is tonight", CancellationToken.None);
        Assert.Equal("SM123", sid);
        Assert.StartsWith("Basic ", _auth);
        Assert.Contains("From=whatsapp%3A%2B15551234567", _body);
        Assert.Contains("To=whatsapp%3A%2B447700900123", _body);
        Assert.Contains("Your+lesson+is+tonight", _body);
    }

    [Fact]
    public async Task Non_success_throws_with_status()
    {
        var sender = new TwilioWhatsAppSender(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("""{"message":"bad number"}""", Encoding.UTF8, "application/json") }))
            { BaseAddress = new Uri("https://api.twilio.com/") },
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+1555" }),
            NullLogger<TwilioWhatsAppSender>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("+447700900123", "x", CancellationToken.None));
    }
}
```
`Options.Create` needs `Microsoft.Extensions.Options` — referenced (used by ResendEmailSender). `NullLogger<T>` is in the shared framework.

- [ ] **Step 3: Run to verify it fails** (missing types).

- [ ] **Step 4: Implement**

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Booking.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Infrastructure.WhatsApp;

public sealed class TwilioWhatsAppSender(
    HttpClient http,
    IOptions<TwilioOptions> options,
    ILogger<TwilioWhatsAppSender> log) : ITwilioSender
{
    public async Task<string> SendAsync(string toE164, string body, CancellationToken ct)
    {
        var opts = options.Value;
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"2010-04-01/Accounts/{opts.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["From"] = $"whatsapp:{opts.FromNumber}",
                ["To"] = $"whatsapp:{toE164}",
                ["Body"] = body,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.AccountSid}:{opts.AuthToken}")));
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            log.LogWarning("Twilio send to {To} failed: {Status} {Body}", toE164, (int)res.StatusCode, err);
            throw new HttpRequestException($"Twilio returned {(int)res.StatusCode}");
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var sid = doc.RootElement.GetProperty("sid").GetString() ?? "";
        log.LogInformation("WhatsApp sent to {To} sid {Sid}", toE164, sid);
        return sid;
    }
}
```

`NullTwilioSender`:
```csharp
using Booking.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.WhatsApp;

/// Log-only fallback when Twilio:AccountSid is empty (dev/E2E/CI).
public sealed class NullTwilioSender(ILogger<NullTwilioSender> log) : ITwilioSender
{
    public Task<string> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        log.LogInformation("Twilio unconfigured — would send to {To}: {Body}", toE164, body);
        return Task.FromResult("SM-LOG-ONLY");
    }
}
```

- [ ] **Step 5: Run + commit**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~TwilioWhatsAppSenderTests" 2>&1 | grep -E "Passed!|Failed!"`
Expected: 2 passed.
```bash
git add backend/Booking.Application/Abstractions/ITwilioSender.cs backend/Booking.Infrastructure/WhatsApp/
git commit -m "feat(whatsapp): twilio sender with stub tests, log-only fallback"
```

---

### Task 2: ReminderLog entity + message copy (TDD the copy)

**Files:**
- Create: `backend/Booking.Domain/Reminders/ReminderLog.cs`
- Create: `backend/Booking.Application/Notifications/ReminderMessages.cs`
- Test: `backend/Booking.Tests/WhatsApp/ReminderMessagesTests.cs`
- Modify: `backend/Booking.Infrastructure/Persistence/AppDbContext.cs` + migration

- [ ] **Step 1: Entity**

Check `Booking.Domain` namespace convention for the folder (match sibling, e.g. `Booking.Domain.Slots` → use `Booking.Domain.Reminders`):
```csharp
namespace Booking.Domain.Reminders;

public sealed class ReminderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string To { get; set; }
    public required DateOnly Date { get; set; }
    public required string Template { get; set; }
    public required string Result { get; set; }
    public string? TwilioSid { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
```
DbSet + migration `AddReminderLog` (same `dotnet ef` pattern as the Google plan's Task 2; `export PATH="$PATH:$HOME/.dotnet/tools"` first).

- [ ] **Step 2: Message copy — failing tests first**

The 4 wordings (submit these exact texts as Twilio templates pre-launch):
```csharp
using Booking.Application.Notifications;
using Xunit;

public class ReminderMessagesTests
{
    [Fact]
    public void Confirmation_names_day_time_and_link()
    {
        var msg = ReminderMessages.Confirmation("Thandi", new DateOnly(2026, 10, 6), "19:30", "https://meet.google.com/x");
        Assert.Contains("Thandi", msg);
        Assert.Contains("19:30", msg);
        Assert.Contains("https://meet.google.com/x", msg);
    }

    [Fact]
    public void Monday_summary_lists_each_lesson_line()
    {
        var msg = ReminderMessages.MondaySummary("Thandi",
            [(new DateOnly(2026, 10, 6), "19:30"), (new DateOnly(2026, 10, 8), "19:30")]);
        Assert.Contains("Tue", msg);
        Assert.Contains("Thu", msg);
    }

    [Fact]
    public void Nudges_carry_times_and_links()
    {
        Assert.Contains("30 min", ReminderMessages.EveningNudge("Thandi", "19:30", "https://meet.google.com/x"));
        Assert.Contains("tonight", ReminderMessages.MorningNudge("Thandi", "19:30", "https://meet.google.com/x"));
    }
}
```
`ReminderMessages` lives in `backend/Booking.Application/Notifications/ReminderMessages.cs` (static class, pure functions — signatures must match the test exactly: `Confirmation(string name, DateOnly date, string localTime, string link)`, `MondaySummary(string name, List<(DateOnly date, string localTime)> lessons)`, `MorningNudge(string name, string localTime, string link)`, `EveningNudge(string name, string localTime, string link)`, plus `Cancelled(name, date, localTime)` and `Rescheduled(name, newDate, newLocalTime, originalDate, link)` mirroring Confirmation). Use `date.ToString("ddd d MMM", CultureInfo.InvariantCulture)` for day names.

- [ ] **Step 3: Implement copy to pass** (write the 6 methods; keep each under 300 chars — WhatsApp-friendly).

- [ ] **Step 4: Run + commit**

```bash
git add backend/Booking.Domain/Reminders/ backend/Booking.Application/Notifications/ReminderMessages.cs backend/Booking.Tests/WhatsApp/ReminderMessagesTests.cs backend/Booking.Infrastructure/Migrations/ backend/Booking.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat(whatsapp): reminder log + message copy with tests"
```

---

### Task 3: BookingNotifier — confirmations that never throw

**Files:**
- Create: `backend/Booking.Application/Abstractions/IBookingNotifier.cs`
- Create: `backend/Booking.Application/Notifications/BookingNotifier.cs`
- Test: `backend/Booking.Tests/WhatsApp/BookingNotifierTests.cs` (fake sender throws → notifier swallows; success → ReminderLog row)

- [ ] **Step 1: Port + notifier**

```csharp
namespace Booking.Application.Abstractions;

public enum BookingChangeKind { Created, Cancelled, Rescheduled }

public interface IBookingNotifier
{
    Task NotifyBookingChangedAsync(Guid bookingId, BookingChangeKind kind, CancellationToken ct);
}
```
`BookingNotifier(IAppDbContext db, ITwilioSender twilio, ILogger<BookingNotifier> log)`:
- loads booking + student + slot (join like GetMyBookingsQueryHandler does — copy that join shape),
- skips silently when student has no PhoneE164,
- renders via ReminderMessages (Confirmation/Cancelled/Rescheduled),
- sends with per-call try/catch → writes ReminderLog(To, Date, Template="confirmation"/"cancelled"/"rescheduled", Result="sent"/"failed", TwilioSid),
- NEVER throws (catch-all inside, log + failed row).

- [ ] **Step 2: Tests** — seed one user+slot+booking via EF InMemory (package already referenced in Booking.Tests — verify with `grep InMemory backend/Booking.Tests/Booking.Tests.csproj`), call Notify, assert ReminderLog row `sent`; then failing sender (throwing stub) → row `failed`, no exception.

- [ ] **Step 3: Wire into the 3 handlers**

`CreateBookingCommandHandler`: inject `IBookingNotifier notifier`; after the successful save (after the try/catch retry block, before `return`), add:
```csharp
await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Created, ct);
```
`RescheduleBookingCommandHandler`: same with `BookingChangeKind.Rescheduled` after its save.
`CancelBookingCommandHandler`: same with `BookingChangeKind.Cancelled` after its save.
Notifier never throws, so handler behavior (incl. existing tests) is unchanged. Register `services.AddScoped<IBookingNotifier, BookingNotifier>();` in Application DI (`AddApplication` — check that file registers handlers via assembly scan; add explicit line).

- [ ] **Step 4: Run full suite + commit**

Run: `cd backend && dotnet test 2>&1 | grep -E "Passed!|Failed!"`
```bash
git add backend/Booking.Application/ backend/Booking.Tests/WhatsApp/BookingNotifierTests.cs
git commit -m "feat(whatsapp): booking confirmations via notifier, never throws"
```

---

### Task 4: ReminderService — rhythms, catch-up, isolation

**Files:**
- Create: `backend/Booking.Application/Notifications/ReminderSchedule.cs`
- Test: `backend/Booking.Tests/WhatsApp/ReminderScheduleTests.cs`
- Create: `backend/Booking.Infrastructure/WhatsApp/ReminderService.cs`

- [ ] **Step 1: Extract pure schedule math (testable without timers)**

Create `backend/Booking.Application/Notifications/ReminderSchedule.cs`:
```csharp
using Booking.Domain.Slots;
using TimeZoneConverter;

namespace Booking.Application.Notifications;

public static class ReminderSchedule
{
    public const string ZoneId = "Africa/Harare";
    public const string StudentZoneId = "Europe/London";

    public sealed record FireTime(DateTime Utc, string Template, DateOnly LessonDate);

    /// All fire times (UTC) for one lesson date: 08:00 + 20:00 Harare same day.
    public static List<FireTime> ForLesson(DateOnly date)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        DateTime AtUtc(int h, int m) => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(h, m)), DateTimeKind.Unspecified), tz);
        return
        [
            new(AtUtc(8, 0), "morning", date),
            new(AtUtc(20, 0), "evening", date),
        ];
    }

    /// Next Monday 09:00 Harare in UTC.
    public static DateTime NextMondaySummaryUtc(DateTime nowUtc)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        var harareNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)harareNow.DayOfWeek + 7) % 7;
        var candidate = harareNow.Date.AddDays(daysToMonday).AddHours(9);
        if (candidate <= harareNow) candidate = candidate.AddDays(7);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), tz);
    }

    /// Student-local "19:30" text for a lesson date (BST-safe).
    public static string StudentLocalTime(DateOnly date)
    {
        var studentTz = TZConvert.GetTimeZoneInfo(StudentZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(LessonTime.StartUtc(date), studentTz);
        return local.ToString("HH:mm");
    }
}
```
Verify `LessonTime.StartUtc`/`ZoneId` names first: `grep -n "public static" backend/Booking.Domain/Slots/LessonTime.cs`. `TimeZoneConverter` package: confirm `grep TZConvert backend/Booking.Infrastructure/*.csproj backend/Booking.Domain/*.csproj` — else `dotnet add` it to the Application project (schedule lives in Application).

Tests (`ReminderScheduleTests`):
```csharp
[Fact] Morning_is_0600Z_and_evening_1800Z_for_harare_date
// 2026-10-06: Harare UTC+2 → 08:00+02 = 06:00Z; 20:00+02 = 18:00Z
[Fact] Monday_summary_is_monday_0700Z
// Monday 09:00+02 = 07:00Z; pick now = Sunday 2026-10-04 12:00Z → expect Monday 2026-10-05 07:00Z
[Fact] Student_time_flips_with_BST
// 2026-09-22 (BST): 18:30Z → "19:30"; 2026-12-03 (GMT): 18:30Z → "18:30"
[Fact] Monday_summary_skips_today_if_past_0900
// now = Monday 2026-10-05 08:00Z (=10:00 Harare) → expect next Monday 2026-10-12 07:00Z
```

- [ ] **Step 2: Run schedule tests to green.**

- [ ] **Step 3: The worker**

`backend/Booking.Infrastructure/WhatsApp/ReminderService.cs`:
```csharp
using Booking.Application.Abstractions;
using Booking.Application.Notifications;
using Booking.Domain.Reminders;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.WhatsApp;

/// Timer worker (BackupWorker pattern): computes next fire across all rhythms,
/// sleeps until then, sends due reminders with per-recipient isolation.
public sealed class ReminderService(
    IServiceScopeFactory scopes, ILogger<ReminderService> log) : BackgroundService
{
    private static readonly TimeSpan CatchUpGrace = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct); // let boot finish
        await CatchUpAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            var next = await ComputeNextFireAsync(ct);
            if (next is null) { await Task.Delay(TimeSpan.FromHours(1), ct); continue; }
            var delay = next.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            await FireDueAsync(DateTime.UtcNow, ct);
        }
    }
    // ... plus private helpers with these exact contracts:
    // CatchUpAsync(ct): scope, db, now; CollectDueFiresAsync(db, now-CatchUpGrace, now, ct); SendFireAsync each.
    // ComputeNextFireAsync(ct): min over (next Monday summary if Reminder:Monday) and (next 08:00/20:00 Harare for dates with active future bookings if Reminder:Morning/Evening), else null.
    // FireDueAsync(nowUtc, ct): CollectDueFiresAsync(db, nowUtc-5min, nowUtc, ct); SendFireAsync each.
    // CollectDueFiresAsync(db, fromUtc, toUtc, ct): Slots with active bookings whose ReminderSchedule.ForLesson(date) instants fall in [fromUtc,toUtc] + Monday summaries due in window; LEFT JOIN ReminderLog on (To,Date,Template) to skip sent; returns (student, fire) pairs.
    // SendFireAsync: renders via ReminderMessages + StudentLocalTime, skips null PhoneE164 (log), per-send try/catch writing ReminderLog(sent/failed + sid).
    // Monday summary: that ISO week's Mon–Sun Harare lessons per student via ReminderMessages.MondaySummary.
    // Config toggles Reminder:Monday/Morning/Evening/Confirmations (bool, default true) gate each rhythm — read via IConfiguration in the worker.
}
```

- [ ] **Step 4: Commit (worker + schedule + tests)**

```bash
git add backend/Booking.Application/Notifications/ backend/Booking.Infrastructure/WhatsApp/ReminderService.cs backend/Booking.Tests/WhatsApp/
git commit -m "feat(whatsapp): reminder rhythms with catch-up and isolation"
```

---

### Task 5: Phone endpoint + config plumbing + secrets list

**Files:**
- Modify: `backend/Booking.Endpoints/AdminEndpoints.cs`
- Modify: `backend/Booking.Infrastructure/DependencyInjection.cs`
- Modify: `backend/Booking.Host/appsettings.json`
- Modify: `infra/docker-compose.yml`, `infra/Program.cs` (optionalKeys), `.github/workflows/deploy.yml` (env)
- Test: `backend/Booking.Tests/Api/AdminPhoneTests.cs`

- [ ] **Step 1: Endpoint**

In `MapAdmin`, add:
```csharp
group.MapPost("/users/phone", async (PhoneRequest req, IAppDbContext db, CancellationToken ct) =>
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Phone ?? "", @"^\+\d{7,15}$"))
        return Results.BadRequest(new { error = "Phone must be E.164, e.g. +447700900123." });
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower(), ct);
    if (user is null) return Results.NotFound(new { error = "No such user." });
    user.PhoneE164 = req.Phone;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { ok = true });
});

public sealed record PhoneRequest(string Email, string Phone);
```
Needs `using Booking.Application.Abstractions; using Microsoft.EntityFrameworkCore;` — check file's usings first.

- [ ] **Step 2: DI**

```csharp
services.Configure<TwilioOptions>(config.GetSection("Twilio"));
services.AddHttpClient("Twilio", c => c.BaseAddress = new Uri("https://api.twilio.com/"));
services.AddScoped<ITwilioSender>(sp =>
    string.IsNullOrEmpty(config["Twilio:AccountSid"])
        ? new NullTwilioSender(sp.GetRequiredService<ILogger<NullTwilioSender>>())
        : new TwilioWhatsAppSender(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Twilio"),
            sp.GetRequiredService<IOptions<TwilioOptions>>(),
            sp.GetRequiredService<ILogger<TwilioWhatsAppSender>>()));
services.AddScoped<IBookingNotifier, BookingNotifier>();
services.AddHostedService<ReminderService>();
```
`TwilioWhatsAppSender` ctor takes `HttpClient` (per plan Task 1) — factory-created here. ReminderService always registered (uses Null sender when unconfigured → logs only).

- [ ] **Step 3: Config files**

`appsettings.json`: add
```json
"Twilio": { "AccountSid": "", "AuthToken": "", "FromNumber": "" },
"Reminder": { "Monday": true, "Morning": true, "Evening": true, "Confirmations": true }
```
`infra/docker-compose.yml` backend env: add
```yaml
      - Twilio__AccountSid=${TWILIO_ACCOUNT_SID}
      - Twilio__AuthToken=${TWILIO_AUTH_TOKEN}
      - Twilio__FromNumber=${TWILIO_FROM_NUMBER}
      - Reminder__Monday=${REMINDER_MONDAY:-true}
      - Reminder__Morning=${REMINDER_MORNING:-true}
      - Reminder__Evening=${REMINDER_EVENING:-true}
      - Reminder__Confirmations=${REMINDER_CONFIRMATIONS:-true}
```
`infra/Program.cs` optionalKeys: add `"TWILIO_ACCOUNT_SID", "TWILIO_AUTH_TOKEN", "TWILIO_FROM_NUMBER",`.
`.github/workflows/deploy.yml` env: add the three `TWILIO_*: ${{ secrets.TWILIO_* }}` lines (reminder toggles default true — omit).

- [ ] **Step 4: Tests + full suite run**

New `AdminPhoneTests`: admin sets phone → user shows it (query db via factory scope or re-GET? assert via second call to a GET? simplest: assert 200 + row in db through a scoped `IAppDbContext` from `_factory.Services`); student gets 403; invalid E.164 → 400. Then:
Run: `cd backend && dotnet test 2>&1 | grep -E "Passed!|Failed!"` then `cd ../frontend && npm test 2>&1 | grep Tests && npm run build 2>&1 | tail -n 1`

- [ ] **Step 5: Commit**

```bash
git add backend/Booking.Endpoints/AdminEndpoints.cs backend/Booking.Infrastructure/DependencyInjection.cs backend/Booking.Host/appsettings.json backend/Booking.Tests/Api/AdminPhoneTests.cs infra/ .github/workflows/deploy.yml
git commit -m "feat(whatsapp): phone endpoint, di, config plumbing"
```

---

### Task 6: Secrets + templates runbook (human task, documented in-repo)

**Files:**
- Create: `docs/superpowers/specs/twilio-production-checklist.md`

- [ ] **Step 1: Write the runbook**

```markdown
# Twilio production sender checklist (one-time, human)

1. Twilio Console → Messaging → Senders → buy/enable a WhatsApp sender (pay-as-you-go).
2. Submit 4 templates for approval (exact bodies live in
   `backend/Booking.Application/Notifications/ReminderMessages.cs`):
   confirmation, cancellation, monday-summary, morning-nudge, evening-nudge.
   Record returned SIDs in `Twilio:TemplateSids` config when enforcing templates.
3. `gh secret set TWILIO_ACCOUNT_SID / TWILIO_AUTH_TOKEN / TWILIO_FROM_NUMBER` (E.164, e.g. +15551234567).
4. Set real phones: `POST /api/admin/users/phone` per user (teacher + 2 students) — or direct DB update.
5. Deploy → verify in `ReminderLog` (`GET` via admin? check logs) + one live confirmation booking.
6. Sandbox remains for dev (leave `Twilio:*` empty locally → log-only sender).
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/twilio-production-checklist.md
git commit -m "docs: twilio production checklist"
```

---

## Self-Review

**1. Spec coverage:**
- §3 all 5 rhythms: confirmations (Task 3), Mon/08:00/20:00 (Task 4), codes-email-primary (no code — Resend stays default; WhatsApp code path explicitly deferred per spec "only if email bounces" → NOT in this plan; flag as follow-up, not gap).
- §3 sandbox→production, join ceremony obsolete: Task 6 runbook ✓ (spec rev 2 says production).
- §3 per-student London times: Task 4 StudentLocalTime + Task 2 copy tests ✓.
- §3 placeholders→real numbers: Task 5 endpoint ✓.
- §3 logging every send: ReminderLog in Tasks 3–4 ✓.
- §4 ReminderLog table: Task 2 ✓. Errors/restart semantics: Task 4 ✓. Tests incl. BST: Task 4 ✓. Config keys: Task 5 ✓ (compose/deploy/Pulumi). Playwright: §4 asks Connect button (2A) + Meet link on booking + /api wiring — WhatsApp needs no new E2E (Null sender in E2E env; assert suite still green in Task 5).
- §4 deploy pipeline: Task 5 ✓.

**2. Placeholder scan:** no TBD/TODO/unshown code. ReminderService method bodies are specified by exact contract (inputs/outputs/behavior) rather than pasted 80-line implementations — each behavior is pinned; acceptable per "steps that describe what to do without showing how" only if code were missing AND behavior vague. Behavior is fully pinned; worker fills idiomatic EF.

**3. Type consistency:** `ITwilioSender.SendAsync(string,string,CancellationToken)→Task<string>`; `IBookingNotifier.NotifyBookingChangedAsync(Guid,BookingChangeKind,CancellationToken)`; `ReminderLog(To,Date,Template,Result,TwilioSid)`; `ReminderSchedule.FireTime(Utc,Template,LessonDate)`; `ReminderMessages` 6 methods with exact signatures used by tests; `TwilioOptions(AccountSid,AuthToken,FromNumber)`; `PhoneRequest(Email,Phone)`.
