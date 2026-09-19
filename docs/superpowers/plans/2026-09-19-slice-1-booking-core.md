# Slice 1 — Booking Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the working booking system (slice 1): shared calendar with bookings/cancel/reschedule, magic-code auth, admin block calendar, fixed Meet link + .ics, R2 backups, Docker + Pulumi deploy to the existing droplet, full CI with Playwright E2E.

**Architecture:** C# Minimal API in Clean Architecture layers (Domain/Application/Infrastructure/Endpoints/Host) mirroring `erpnext-dashboard/backend`; Vue 3 + TS + Nuxt UI (Vite) SPA; SQLite via EF Core; all instants UTC, lesson anchored 20:30 `Africa/Harare`.

**Tech Stack:** .NET 10, EF Core 9 (SQLite), custom `ICommand/IQuery` dispatcher (no MediatR license), TimeZoneConverter, AWSSDK.S3 (Cloudflare R2), Vue 3 + Vite + @nuxt/ui v4 + vue-router, Vitest, Playwright, Docker, Pulumi C# (Command/SSH), GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-19-class-booking-design.md` — slice 2 (Google OAuth + Twilio WhatsApp scheduler) is a separate later plan.

**Conventions from reference repo** (`erpnext-dashboard/backend`): feature folders in Application (`XxxFeature/Command.cs + Handler.cs`), DTOs in `Application/DTOs`, endpoints in `Endpoints/`, DI extensions per layer (`AddApplication`, `AddInfrastructure`), `ISender`-style dispatch.

---

### Task 0: Repo scaffold

**Files:**
- Create: `.gitignore`, `README.md`, `LICENSE.md`

- [ ] **Step 1: Write .gitignore**

```gitignore
# backend
backend/**/bin/
backend/**/obj/
backend/data/
*.user

# frontend
frontend/node_modules/
frontend/dist/

# e2e
e2e/.playwright/
e2e/test-results/
e2e/playwright-report/

# infra
infra/.pulumi/
infra/*.state
infra/.env

# misc
.superpowers/
*.db
*.db-shm
*.db-wal
.DS_Store
```

- [ ] **Step 2: Write LICENSE.md (MIT)**

```text
MIT License

Copyright (c) 2026 <run `git config user.name` and insert real name>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 3: Write README.md**

```markdown
# Class Booking System

Evening programming lessons: shared booking calendar for 2 UK-based students + teacher (20:30 Africa/Harare, Mon–Sat).

## Dev quickstart

    # backend (http://localhost:8080)
    dotnet run --project backend/Host

    # frontend (http://localhost:5173, proxies /api -> 8080)
    cd frontend && npm install && npm run dev

    # e2e (starts both + runs Playwright)
    cd e2e && npm install && npx playwright install --with-deps && npm test

## Layout

    backend/   C# Minimal API (Clean Architecture)
    frontend/  Vue 3 + TS + Nuxt UI
    e2e/       Playwright journeys
    infra/     Pulumi C# + docker-compose deploy
    docs/      specs + plans
```

- [ ] **Step 4: Commit**

```bash
git add .gitignore README.md LICENSE.md && git commit -m "chore: repo scaffold"
```

---

### Task 1: Backend solution + projects

**Files:**
- Create: `backend/BookingApi.slnx`, 6 csproj files, `Host/Program.cs` (placeholder-free minimal)

- [ ] **Step 1: Create solution + projects**

```bash
mkdir -p backend && cd backend
dotnet new sln -n BookingApi && mv BookingApi.slnx BookingApi.slnx 2>/dev/null || true
dotnet new classlib -n Booking.Domain -f net10.0
dotnet new classlib -n Booking.Application -f net10.0
dotnet new classlib -n Booking.Infrastructure -f net10.0
dotnet new classlib -n Booking.Endpoints -f net10.0
dotnet new web -n Booking.Host -f net10.0
dotnet new xunit -n Booking.Tests -f net10.0
dotnet sln add Booking.Domain Booking.Application Booking.Infrastructure Booking.Endpoints Booking.Host Booking.Tests
```

(If `dotnet new sln` produced `BookingApi.slnx` keep it; delete the extra one created by `mv` fallback. Final state: one solution file containing all 6 projects.)

- [ ] **Step 2: Wire project references**

```bash
dotnet add Booking.Application reference Booking.Domain
dotnet add Booking.Infrastructure reference Booking.Domain
dotnet add Booking.Endpoints reference Booking.Application Booking.Domain
dotnet add Booking.Host reference Booking.Application Booking.Domain Booking.Infrastructure Booking.Endpoints
dotnet add Booking.Tests reference Booking.Application Booking.Domain Booking.Infrastructure Booking.Endpoints Booking.Host
```

- [ ] **Step 3: Add packages**

```bash
dotnet add Booking.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite --version 9.0.4
dotnet add Booking.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 9.0.4
dotnet add Booking.Infrastructure package TimeZoneConverter --version 6.1.0
dotnet add Booking.Infrastructure package AWSSDK.S3 --version 3.7.415
dotnet add Booking.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.0
dotnet add Booking.Tests package Microsoft.EntityFrameworkCore.InMemory --version 9.0.4
```

(If `10.0.0`/`9.0.4`/exact patch versions are unavailable, use latest matching major. Record chosen versions in commit message.)

- [ ] **Step 4: Minimal Program.cs so `dotnet build` passes**

`backend/Host/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
```

- [ ] **Step 5: Clean class1.cs leftovers**

```bash
rm -f Booking.Domain/Class1.cs Booking.Application/Class1.cs Booking.Infrastructure/Class1.cs Booking.Endpoints/Class1.cs Booking.Tests/UnitTest1.cs
```

- [ ] **Step 6: Verify build**

Run: `dotnet build` (in `backend/`)
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add backend/ && git commit -m "feat(backend): solution scaffold with clean architecture layers"
```

---

### Task 2: Domain — entities + time + booking policy (TDD)

**Files:**
- Create: `backend/Booking.Domain/Users/User.cs`, `backend/Booking.Domain/Users/UserRole.cs`
- Create: `backend/Booking.Domain/Slots/Slot.cs`, `backend/Booking.Domain/Slots/Booking.cs`, `backend/Booking.Domain/Slots/BookingStatus.cs`
- Create: `backend/Booking.Domain/Slots/BlockedDay.cs`
- Create: `backend/Booking.Domain/Slots/LessonTime.cs`, `backend/Booking.Domain/Slots/BookingPolicy.cs`
- Test: `backend/Booking.Tests/Domain/BookingPolicyTests.cs`, `backend/Booking.Tests/Domain/LessonTimeTests.cs`

- [ ] **Step 1: Write failing domain tests**

`backend/Booking.Tests/Domain/LessonTimeTests.cs`:

```csharp
using Booking.Domain.Slots;
using Xunit;

public class LessonTimeTests
{
    [Fact]
    public void StartUtc_is_1830Z_for_any_harare_date() // CAT = UTC+2 year-round
    {
        var utc = LessonTime.StartUtc(new DateOnly(2026, 9, 22));
        Assert.Equal(new DateTime(2026, 9, 22, 18, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void EndUtc_is_two_hours_later()
    {
        Assert.Equal(LessonTime.StartUtc(new DateOnly(2026, 9, 22)).AddHours(2),
                     LessonTime.EndUtc(new DateOnly(2026, 9, 22)));
    }
}
```

`backend/Booking.Tests/Domain/BookingPolicyTests.cs`:

```csharp
using Booking.Domain.Slots;
using Xunit;

public class BookingPolicyTests
{
    private static readonly DateOnly Future = new(2026, 12, 3); // a Thursday

    [Fact]
    public void Free_future_weekday_is_bookable()
        => Assert.Null(BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddDays(-1), []));

    [Fact]
    public void Sunday_is_rejected()
        => Assert.Contains("Sunday", BookingPolicy.Validate(new DateOnly(2026, 12, 6),
            LessonTime.StartUtc(new DateOnly(2026, 12, 6)).AddDays(-1), []));

    [Fact]
    public void Blocked_day_is_rejected()
        => Assert.Contains("unavailable", BookingPolicy.Validate(Future,
            LessonTime.StartUtc(Future).AddDays(-1), [Future]));

    [Fact]
    public void Past_day_is_rejected()
        => Assert.Contains("past", BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddDays(1), []));

    [Fact]
    public void Within_30min_cutoff_is_rejected()
        => Assert.Contains("30", BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddMinutes(-29), []));

    [Fact]
    public void Exactly_30min_before_is_allowed()
        => Assert.Null(BookingPolicy.Validate(Future, LessonTime.StartUtc(Future).AddMinutes(-30), []));
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test --filter "FullyQualifiedName~Domain"`
Expected: compile errors (`Booking.Domain.Slots` not found).

- [ ] **Step 3: Implement entities**

`backend/Booking.Domain/Users/UserRole.cs`:

```csharp
namespace Booking.Domain.Users;

public enum UserRole { Admin = 1, Student = 2 }
```

`backend/Booking.Domain/Users/User.cs`:

```csharp
namespace Booking.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? PhoneE164 { get; set; }
    public UserRole Role { get; set; } = UserRole.Student;
    public required string TimeZoneId { get; set; } = "Europe/London";
}
```

`backend/Booking.Domain/Slots/BookingStatus.cs`:

```csharp
namespace Booking.Domain.Slots;

public enum BookingStatus { Active = 1, Cancelled = 2 }
```

`backend/Booking.Domain/Slots/Slot.cs`:

```csharp
namespace Booking.Domain.Slots;

public sealed class Slot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required DateOnly Date { get; set; }
    public string? MeetLink { get; set; }
    public string? GoogleEventId { get; set; }
    public List<Booking> Bookings { get; set; } = [];
}
```

`backend/Booking.Domain/Slots/Booking.cs`:

```csharp
namespace Booking.Domain.Slots;

public sealed class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid SlotId { get; set; }
    public required Guid StudentId { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Active;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateOnly? OriginalDate { get; set; }
    public Slot Slot { get; set; } = null!;
}
```

`backend/Booking.Domain/Slots/BlockedDay.cs`:

```csharp
namespace Booking.Domain.Slots;

public sealed class BlockedDay
{
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
}
```

- [ ] **Step 4: Implement time + policy**

`backend/Booking.Domain/Slots/LessonTime.cs`:

```csharp
using TimeZoneConverter;

namespace Booking.Domain.Slots;

public static class LessonTime
{
    public const string ZoneId = "Africa/Harare";
    public const int DurationHours = 2;

    public static DateTime StartUtc(DateOnly date)
    {
        var tz = TZConvert.GetTimeZoneInfo(ZoneId);
        var local = date.ToDateTime(new TimeOnly(20, 30));
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
    }

    public static DateTime EndUtc(DateOnly date) => StartUtc(date).AddHours(DurationHours);
}
```

`backend/Booking.Domain/Slots/BookingPolicy.cs`:

```csharp
namespace Booking.Domain.Slots;

public static class BookingPolicy
{
    public const int CutoffMinutes = 30;

    /// Returns null when bookable, else a human-readable error string.
    public static string? Validate(DateOnly date, DateTime nowUtc, IReadOnlyCollection<DateOnly> blockedDays)
    {
        if (date.DayOfWeek == DayOfWeek.Sunday)
            return "Sundays are off — no lessons on Sundays.";
        if (blockedDays.Contains(date))
            return "Teacher is unavailable that day.";
        if (nowUtc >= LessonTime.StartUtc(date))
            return "That lesson is in the past.";
        if (nowUtc > LessonTime.StartUtc(date).AddMinutes(-CutoffMinutes))
            return $"Too late — bookings close {CutoffMinutes} minutes before the lesson starts.";
        return null;
    }
}
```

- [ ] **Step 5: Run tests, verify pass**

Run: `dotnet test --filter "FullyQualifiedName~Domain"`
Expected: 8 passed.

- [ ] **Step 6: Commit**

```bash
git add backend/ && git commit -m "feat(domain): entities, harare-anchored lesson time, booking policy"
```

---

### Task 3: Application core — abstractions + DTOs + handlers

**Files:**
- Create: `backend/Booking.Application/Abstractions/Command.cs`, `backend/Booking.Application/Abstractions/Query.cs`, `backend/Booking.Application/Abstractions/ISender.cs` (single file OK)
- Create: `backend/Booking.Application/Abstractions/IGoogleAuthUser.cs` — NO, current-user access via `ICurrentUser` in `Abstractions/ICurrentUser.cs`
- Create: `backend/Booking.Application/DTOs/SlotDayDto.cs`, `backend/Booking.Application/DTOs/BookingDto.cs`, `backend/Booking.Application/DTOs/UserDto.cs`
- Create: `backend/Booking.Application/Slots/GetMonthQuery.cs` + `GetMonthQueryHandler.cs`
- Create: `backend/Booking.Application/Bookings/CreateBookingCommand.cs` + handler, `CancelBookingCommand.cs` + handler, `RescheduleBookingCommand.cs` + handler, `GetMyBookingsQuery.cs` + handler
- Create: `backend/Booking.Application/BlockedDays/BlockDayCommand.cs` + handler, `UnblockDayCommand.cs` + handler
- Create: `backend/Booking.Application/DependencyInjection.cs`
- Test: `backend/Booking.Tests/Application/BookingHandlerTests.cs`

- [ ] **Step 1: Write dispatch abstractions**

`backend/Booking.Application/Abstractions/Dispatch.cs`:

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application.Abstractions;

public interface ICommand<TResponse> where TResponse : notnull { }

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse> where TResponse : notnull
{
    Task<TResponse> Handle(TCommand command, CancellationToken ct);
}

public interface IQuery<TResponse> where TResponse : notnull { }

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse> where TResponse : notnull
{
    Task<TResponse> Handle(TQuery query, CancellationToken ct);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}

public sealed class Sender(IServiceProvider sp) : ISender
{
    public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
        => InvokeHandler(command, typeof(ICommandHandler<,>), ct);

    public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
        => InvokeHandler(query, typeof(IQueryHandler<,>), ct);

    private Task<TResponse> InvokeHandler<TResponse>(object message, Type openGeneric, CancellationToken ct)
    {
        var concrete = openGeneric.MakeGenericType(message.GetType(), typeof(TResponse));
        var instance = sp.GetRequiredService(concrete);
        var method = concrete.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance)
                     ?? throw new InvalidOperationException($"No Handle on {concrete.Name}");
        return (Task<TResponse>)(method.Invoke(instance, [message, ct])
            ?? throw new InvalidOperationException("Handle returned null"));
    }
}

public static class HandlerRegistration
{
    public static IServiceCollection AddHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var asm in assemblies)
        foreach (var type in asm.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        foreach (var iface in type.GetInterfaces().Where(i =>
                     i.IsGenericType &&
                     i.GetGenericTypeDefinition() is var g &&
                     (g == typeof(ICommandHandler<,>) || g == typeof(IQueryHandler<,>))))
            services.AddScoped(iface, type);

        services.AddScoped<ISender, Sender>();
        return services;
    }
}
```

`backend/Booking.Application/Abstractions/ICurrentUser.cs`:

```csharp
namespace Booking.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Name { get; }
    bool IsAdmin { get; }
}
```

- [ ] **Step 2: Write DTOs**

`backend/Booking.Application/DTOs/UserDto.cs`:

```csharp
namespace Booking.Application.DTOs;

public sealed record UserDto(Guid Id, string Name, string Email, string Role);
```

`backend/Booking.Application/DTOs/SlotDayDto.cs`:

```csharp
namespace Booking.Application.DTOs;

public enum DayState { Bookable, Booked, Combined, Sunday, Blocked, Past, Cutoff }

public sealed record SlotDayDto(
    DateOnly Date,
    DateTime StartUtc,
    DateTime EndUtc,
    DayState State,
    bool CanBook,
    string? Reason,
    List<string> StudentNames,
    string? MeetLink);
```

`backend/Booking.Application/DTOs/BookingDto.cs`:

```csharp
namespace Booking.Application.DTOs;

public sealed record BookingDto(
    Guid Id,
    DateOnly Date,
    DateTime StartUtc,
    string StudentName,
    bool CanCancel,
    bool CanReschedule,
    DateOnly? OriginalDate,
    string? MeetLink);
```

- [ ] **Step 3: Define the persistence port + domain records (needed by handlers below)**

`backend/Booking.Application/Abstractions/IAppDbContext.cs` (Application defines the port; Infrastructure implements):

```csharp
using Booking.Domain.Auth;
using Booking.Domain.Backups;
using Booking.Domain.Slots;
using Booking.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Slot> Slots { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BlockedDay> BlockedDays { get; }
    DbSet<AuthCode> AuthCodes { get; }
    DbSet<BackupLog> BackupLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

Two small Domain files:

```csharp
// backend/Booking.Domain/Auth/AuthCode.cs
namespace Booking.Domain.Auth;

public sealed class AuthCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
```

```csharp
// backend/Booking.Domain/Backups/BackupLog.cs
namespace Booking.Domain.Backups;

public sealed class BackupLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime RanAtUtc { get; set; } = DateTime.UtcNow;
    public required string ObjectKey { get; set; }
    public long SizeBytes { get; set; }
}
```

- [ ] **Step 4: Write month query + handler**

`backend/Booking.Application/Slots/GetMonthQuery.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.Slots;

public sealed record GetMonthQuery(int Year, int Month) : IQuery<List<SlotDayDto>>;
```

`backend/Booking.Application/Slots/GetMonthQueryHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Slots;

public sealed class GetMonthQueryHandler(IAppDbContext db) : IQueryHandler<GetMonthQuery, List<SlotDayDto>>
{
    public async Task<List<SlotDayDto>> Handle(GetMonthQuery q, CancellationToken ct)
    {
        var first = new DateOnly(q.Year, q.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var blocked = await db.BlockedDays
            .Where(b => b.Date >= first && b.Date <= last)
            .Select(b => b.Date).ToListAsync(ct);
        var blockedSet = blocked.ToHashSet();

        var slots = await db.Slots
            .Include(s => s.Bookings)
            .Where(s => s.Date >= first && s.Date <= last)
            .ToListAsync(ct);

        var activeByDate = slots
            .SelectMany(s => s.Bookings.Where(b => b.Status == BookingStatus.Active)
                .Select(b => (s.Date, b.StudentId)))
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.Select(x => x.StudentId).ToList());

        var names = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var now = DateTime.UtcNow;
        var days = new List<SlotDayDto>();

        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var (state, reason) = Classify(d, blockedSet, now, activeByDate.GetValueOrDefault(d));
            var studentIds = activeByDate.GetValueOrDefault(d) ?? [];
            days.Add(new SlotDayDto(
                Date: d,
                StartUtc: LessonTime.StartUtc(d),
                EndUtc: LessonTime.EndUtc(d),
                State: state,
                CanBook: state == DayState.Bookable,
                Reason: reason,
                StudentNames: studentIds.Select(id => names.GetValueOrDefault(id, "?")).ToList(),
                MeetLink: slots.FirstOrDefault(s => s.Date == d)?.MeetLink));
        }
        return days;
    }

    internal static (DayState, string?) Classify(DateOnly d, HashSet<DateOnly> blocked, DateTime now, List<Guid>? activeIds)
    {
        if (d.DayOfWeek == DayOfWeek.Sunday) return (DayState.Sunday, "No lessons on Sundays");
        if (blocked.Contains(d)) return (DayState.Blocked, "Teacher unavailable");
        if (now >= LessonTime.StartUtc(d)) return (DayState.Past, "Lesson already passed");
        if (now > LessonTime.StartUtc(d).AddMinutes(-BookingPolicy.CutoffMinutes))
            return (DayState.Cutoff, "Booking window closed");
        return activeIds switch
        {
            null or [] => (DayState.Bookable, null),
            { Count: 1 } => (DayState.Booked, null),
            _ => (DayState.Combined, null)
        };
    }
}
```

- [ ] **Step 4: Write booking commands + handlers**

`backend/Booking.Application/Bookings/CreateBookingCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.Bookings;

public sealed record CreateBookingCommand(DateOnly Date) : ICommand<BookingDto>;
```

`backend/Booking.Application/Bookings/CreateBookingCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Application.Slots;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class CreateBookingCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<CreateBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(CreateBookingCommand c, CancellationToken ct)
    {
        var blocked = await db.BlockedDays.Select(b => b.Date).ToListAsync(ct);
        var error = BookingPolicy.Validate(c.Date, DateTime.UtcNow, blocked);
        if (error is not null) throw new BookingException(error);

        var existingActive = await db.Bookings.AnyAsync(b =>
            b.StudentId == user.UserId && b.Status == BookingStatus.Active &&
            b.Slot.Date == c.Date, ct);
        if (existingActive) throw new BookingException("You already have a booking on that day.");

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct)
                   ?? new Slot { Date = c.Date };
        if (slot.Id == Guid.Empty) db.Slots.Add(slot);

        var booking = new Booking { SlotId = slot.Id, StudentId = user.UserId };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);

        return new BookingDto(booking.Id, c.Date, LessonTime.StartUtc(c.Date), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: null, MeetLink: slot.MeetLink);
    }
}
```

`backend/Booking.Application/Bookings/BookingException.cs`:

```csharp
namespace Booking.Application.Bookings;

public sealed class BookingException(string message) : Exception(message);
```

`backend/Booking.Application/Bookings/CancelBookingCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.Bookings;

public sealed record CancelBookingCommand(Guid BookingId) : ICommand<bool>;
```

`backend/Booking.Application/Bookings/CancelBookingCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class CancelBookingCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<CancelBookingCommand, bool>
{
    public async Task<bool> Handle(CancelBookingCommand c, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.Id == c.BookingId, ct)
            ?? throw new BookingException("Booking not found.");

        if (booking.StudentId != user.UserId && !user.IsAdmin)
            throw new BookingException("You can only cancel your own bookings.");

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

`backend/Booking.Application/Bookings/RescheduleBookingCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.Bookings;

public sealed record RescheduleBookingCommand(Guid BookingId, DateOnly NewDate) : ICommand<BookingDto>;
```

`backend/Booking.Application/Bookings/RescheduleBookingCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class RescheduleBookingCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<RescheduleBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(RescheduleBookingCommand c, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.Id == c.BookingId, ct)
            ?? throw new BookingException("Booking not found.");

        if (booking.StudentId != user.UserId && !user.IsAdmin)
            throw new BookingException("You can only reschedule your own bookings.");

        var blocked = await db.BlockedDays.Select(b => b.Date).ToListAsync(ct);
        var error = BookingPolicy.Validate(c.NewDate, DateTime.UtcNow, blocked);
        if (error is not null) throw new BookingException(error);

        var clash = await db.Bookings.AnyAsync(b =>
            b.StudentId == booking.StudentId && b.Id != booking.Id &&
            b.Status == BookingStatus.Active && b.Slot.Date == c.NewDate, ct);
        if (clash) throw new BookingException("You already have a booking on the new day.");

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct)
                   ?? new Slot { Date = c.NewDate };
        if (slot.Id == Guid.Empty) db.Slots.Add(slot);

        booking.OriginalDate ??= booking.Slot.Date;
        booking.SlotId = slot.Id;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new BookingDto(booking.Id, c.NewDate, LessonTime.StartUtc(c.NewDate), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: booking.OriginalDate, MeetLink: slot.MeetLink);
    }
}
```

`backend/Booking.Application/Bookings/GetMyBookingsQuery.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Bookings;

public sealed record GetMyBookingsQuery : IQuery<List<BookingDto>>;
```

`backend/Booking.Application/Bookings/GetMyBookingsQueryHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Bookings;

public sealed class GetMyBookingsQueryHandler(IAppDbContext db, ICurrentUser user)
    : IQueryHandler<GetMyBookingsQuery, List<BookingDto>>
{
    public async Task<List<BookingDto>> Handle(GetMyBookingsQuery q, CancellationToken ct)
    {
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.StudentId == user.UserId && b.Status == BookingStatus.Active)
            .Join(db.Slots, b => b.SlotId, s => s.Id, (b, s) => new { b, s })
            .OrderBy(x => x.s.Date)
            .ToListAsync(ct);

        return rows.Select(x => new BookingDto(
            x.b.Id, x.s.Date, LessonTime.StartUtc(x.s.Date), user.Name,
            CanCancel: true, CanReschedule: true,
            x.b.OriginalDate, x.s.MeetLink)).ToList();
    }
}
```

- [ ] **Step 5: Write blocked-days commands + handlers**

`backend/Booking.Application/BlockedDays/BlockDayCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.BlockedDays;

public sealed record BlockDayCommand(DateOnly Date, string? Reason) : ICommand<bool>;
```

`backend/Booking.Application/BlockedDays/BlockDayCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.BlockedDays;

public sealed class BlockDayCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<BlockDayCommand, bool>
{
    public async Task<bool> Handle(BlockDayCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        if (await db.BlockedDays.AnyAsync(b => b.Date == c.Date, ct)) return true;
        db.BlockedDays.Add(new BlockedDay { Date = c.Date, Reason = c.Reason });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

`backend/Booking.Application/BlockedDays/UnblockDayCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.BlockedDays;

public sealed record UnblockDayCommand(DateOnly Date) : ICommand<bool>;
```

`backend/Booking.Application/BlockedDays/UnblockDayCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.BlockedDays;

public sealed class UnblockDayCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<UnblockDayCommand, bool>
{
    public async Task<bool> Handle(UnblockDayCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        var day = await db.BlockedDays.FirstOrDefaultAsync(b => b.Date == c.Date, ct);
        if (day is not null) { db.BlockedDays.Remove(day); await db.SaveChangesAsync(ct); }
        return true;
    }
}
```

- [ ] **Step 6: DI extension**

`backend/Booking.Application/DependencyInjection.cs`:

```csharp
using Booking.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Booking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
        => services.AddHandlers(Assembly.GetExecutingAssembly());
}
```

- [ ] **Step 7: Build (no tests yet — handlers tested in Task 6 integration tests)**

Run: `dotnet build` (in `backend/`)
Expected: `Build succeeded` (warnings about unused `ICurrentUser` implementations are fine — Infrastructure provides it in Task 5).

- [ ] **Step 8: Commit**

```bash
git add backend/ && git commit -m "feat(application): dispatcher, month query, booking/blocked-day handlers"
```

---

### Task 4: Infrastructure — EF Core, migrations, seeding, meet provider, R2 backup

**Files:**
- Create: `backend/Booking.Infrastructure/Persistence/AppDbContext.cs`, `Persistence/ModelBuilderExtensions.cs`, `Persistence/Seeder.cs`
- Create: `backend/Booking.Infrastructure/Identity/CurrentUser.cs`
- Create: `backend/Booking.Infrastructure/Meet/IMeetProvider.cs`, `Meet/FixedLinkMeetProvider.cs`
- Create: `backend/Booking.Infrastructure/Meet/MeetLinkSetter.cs` (fills slot.MeetLink on booking)
- Create: `backend/Booking.Infrastructure/Backups/IBackupService.cs`, `Backups/R2BackupService.cs`, `Backups/BackupWorker.cs`
- Create: `backend/Booking.Infrastructure/DependencyInjection.cs`
- Modify: `backend/Booking.Application/Bookings/CreateBookingCommandHandler.cs` (set MeetLink on create)

- [ ] **Step 1: DbContext + mappings**

`backend/Booking.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Booking.Domain.Backups;
using Booking.Domain.Slots;
using Booking.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BlockedDay> BlockedDays => Set<BlockedDay>();
    public DbSet<AuthCode> AuthCodes => Set<AuthCode>();
    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Name).HasMaxLength(100);
        });
        mb.Entity<Slot>(e =>
        {
            e.HasIndex(s => s.Date).IsUnique();
            e.Property(s => s.MeetLink).HasMaxLength(300);
        });
        mb.Entity<Booking>(e =>
        {
            e.HasIndex(b => new { b.SlotId, b.StudentId, b.Status }).IsUnique();
            e.HasOne(b => b.Slot).WithMany(s => s.Bookings).HasForeignKey(b => b.SlotId);
        });
        mb.Entity<BlockedDay>(e => e.HasKey(b => b.Date));
        mb.Entity<AuthCode>(e => e.HasIndex(a => a.UserId));
    }
}
```

- [ ] **Step 2: Seeding from config**

`backend/Booking.Infrastructure/Persistence/Seeder.cs`:

```csharp
using Booking.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Booking.Infrastructure.Persistence;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        var section = config.GetSection("App:Users").Get<List<SeedUser>>() ?? [];
        foreach (var seed in section)
        {
            if (await db.Users.AnyAsync(u => u.Email == seed.Email)) continue;
            db.Users.Add(new User
            {
                Name = seed.Name,
                Email = seed.Email,
                PhoneE164 = seed.Phone,
                Role = Enum.TryParse<UserRole>(seed.Role, ignoreCase: true, out var r) ? r : UserRole.Student,
                TimeZoneId = "Europe/London"
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed record SeedUser(string Name, string Email, string? Phone, string Role);
}
```

- [ ] **Step 3: CurrentUser (HttpContext-based)**

`backend/Booking.Infrastructure/Identity/CurrentUser.cs`:

```csharp
using System.Security.Claims;
using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Identity;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id
        : throw new UnauthorizedAccessException("Not signed in.");
    public string Name => Principal?.FindFirst(ClaimTypes.Name)?.Value ?? "?";
    public bool IsAdmin => Principal?.IsInRole("Admin") ?? false;
}
```

- [ ] **Step 4: Meet provider (fixed link, slice 1)**

The port lives in Application so handlers stay Infrastructure-free:

`backend/Booking.Application/Abstractions/IMeetLinkProvider.cs`:

```csharp
namespace Booking.Application.Abstractions;

public interface IMeetLinkProvider
{
    Task<string> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default);
}
```

`backend/Booking.Infrastructure/Meet/FixedLinkMeetProvider.cs` implements it:

```csharp
using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Booking.Infrastructure.Meet;

/// Slice 1: one recurring Meet link from config. Slice 2 swaps in GoogleCalendarMeetProvider.
public sealed class FixedLinkMeetProvider(IConfiguration config) : IMeetLinkProvider
{
    public Task<string> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default)
        => Task.FromResult(config["App:FixedMeetLink"]
           ?? throw new InvalidOperationException("App:FixedMeetLink is not configured."));
}
```

Modify `CreateBookingCommandHandler` — add `IMeetLinkProvider meet` to the primary constructor and after slot resolution (before creating the booking):

```csharp
slot.MeetLink ??= await meet.GetOrCreateLinkAsync(c.Date, ct);
```

- [ ] **Step 5: R2 backup service + worker**

`backend/Booking.Infrastructure/Backups/IBackupService.cs`:

```csharp
namespace Booking.Infrastructure.Backups;

public interface IBackupService
{
    Task BackupNowAsync(CancellationToken ct = default);
    Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default);
    Task RestoreLatestAsync(CancellationToken ct = default);
}
```

`backend/Booking.Infrastructure/Backups/R2BackupService.cs`:

```csharp
using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Transfer;
using Booking.Application.Abstractions;
using Booking.Domain.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Backups;

public sealed class R2BackupService(
    IServiceProvider sp, IConfiguration config, ILogger<R2BackupService> log) : IBackupService
{
    private string DbPath => config["App:DbPath"] ?? "data/booking.db";

    private IAmazonS3 Client()
    {
        var accountId = config["R2:AccountId"] ?? throw new InvalidOperationException("R2:AccountId missing");
        var key = config["R2:AccessKeyId"] ?? throw new InvalidOperationException("R2:AccessKeyId missing");
        var secret = config["R2:SecretAccessKey"] ?? throw new InvalidOperationException("R2:SecretAccessKey missing");
        return new AmazonS3Client(key, secret, new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        });
    }

    public async Task BackupNowAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DbPath))!);
        var tmp = Path.Combine(Path.GetTempPath(), $"booking-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        using (var source = new SqliteConnection($"Data Source={DbPath}"))
        await using (var target = new SqliteConnection($"Data Source={tmp}"))
        {
            await source.OpenAsync(ct); await target.OpenAsync(ct);
            source.BackupDatabase(target);
        }
        var key = $"backups/{DateTime.UtcNow:yyyy/MM/dd/HHmmss}.db.gz";
        await using var gz = File.Create(tmp + ".gz");
        await using (var input = File.OpenRead(tmp))
        await using (var gzip = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.Optimal))
            await input.CopyToAsync(gzip, ct);

        using var transfer = new TransferUtility(Client());
        await transfer.UploadAsync(tmp + ".gz", config["R2:Bucket"], key, ct);
        File.Delete(tmp); File.Delete(tmp + ".gz");

        await using var db = sp.GetRequiredService<AppDbContext>();
        db.BackupLogs.Add(new BackupLog { ObjectKey = key, SizeBytes = new FileInfo(tmp + ".gz").Length });
        await db.SaveChangesAsync(ct);
        log.LogInformation("R2 backup uploaded: {Key}", key);
    }

    public async Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default)
    {
        await using var db = sp.GetRequiredService<AppDbContext>();
        var last = await db.BackupLogs.OrderByDescending(b => b.RanAtUtc).FirstOrDefaultAsync(ct);
        return last?.RanAtUtc;
    }

    public async Task RestoreLatestAsync(CancellationToken ct = default)
    {
        using var s3 = Client();
        var list = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request
        {
            BucketName = config["R2:Bucket"], Prefix = "backups/"
        }, ct);
        var newest = list.S3Objects.OrderByDescending(o => o.LastModified).FirstOrDefault()
                     ?? throw new InvalidOperationException("No backups found in R2.");
        var tmpGz = Path.GetTempFileName();
        await using (var resp = await s3.GetObjectAsync(new Amazon.S3.Model.GetObjectRequest
                     { BucketName = config["R2:Bucket"], Key = newest.Key }, ct))
        await using (var file = File.Create(tmpGz))
            await resp.ResponseStream.CopyToAsync(file, ct);

        var tmp = tmpGz + ".db";
        await using (var gz = File.OpenRead(tmpGz))
        await using (var outDb = File.Create(tmp))
        await using (var gunzip = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionMode.Decompress))
            await gunzip.CopyToAsync(outDb, ct);

        File.Copy(tmp, DbPath, overwrite: true);
        log.LogWarning("Restored DB from {Key}; restarting.", newest.Key);
        Environment.Exit(0); // container restart policy brings the app back, migration runs on boot
    }
}
```

`backend/Booking.Infrastructure/Backups/BackupWorker.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Backups;

/// Nightly backup at 02:00 Africa/Harare (00:00 UTC — CAT is UTC+2 year-round).
public sealed class BackupWorker(IBackupService backups, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        RestoreIfEmpty(ct);
        while (!ct.IsCancellationRequested)
        {
            var next = DateTime.UtcNow.Date.AddHours(24 + 0); // next 00:00 UTC
            if (next <= DateTime.UtcNow) next = next.AddDays(1);
            try
            {
                await Task.Delay(next - DateTime.UtcNow, ct);
                await backups.BackupNowAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log.LogError(ex, "Backup failed; retrying in 1h"); await Task.Delay(TimeSpan.FromHours(1), ct); }
        }
    }

    private void RestoreIfEmpty(CancellationToken ct)
    {
        var dbPath = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build()["App:DbPath"];
        if (!string.IsNullOrEmpty(dbPath) && !File.Exists(dbPath))
        {
            try { backups.RestoreLatestAsync(ct).GetAwaiter().GetResult(); }
            catch (Exception ex) { log.LogWarning(ex, "No R2 backup restored; starting fresh DB."); }
        }
    }
}
```

(Inject `IConfiguration config` into the worker instead of building a local config — replace the body of `RestoreIfEmpty` accordingly:)

```csharp
private void RestoreIfEmpty()
{
    var dbPath = config["App:DbPath"] ?? "data/booking.db";
    if (!File.Exists(dbPath))
    {
        try { backups.RestoreLatestAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { log.LogWarning(ex, "No R2 backup restored; starting fresh DB."); }
    }
}
```

(and change the constructor: `BackupWorker(IBackupService backups, IConfiguration config, ILogger<BackupWorker> log)`; delete the inline version above.)

- [ ] **Step 6: DI wiring**

`backend/Booking.Infrastructure/DependencyInjection.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Infrastructure.Backups;
using Booking.Infrastructure.Identity;
using Booking.Infrastructure.Meet;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var dbPath = config["App:DbPath"] ?? "data/booking.db";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        var conn = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
        services.AddSingleton(conn);
        services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IMeetLinkProvider, FixedLinkMeetProvider>();
        if (!string.IsNullOrEmpty(config["R2:Bucket"]))
        {
            services.AddSingleton<IBackupService, R2BackupService>();
            services.AddHostedService<BackupWorker>();
        }
        return services;
    }

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
    }
}
```

- [ ] **Step 7: Generate migration**

```bash
cd backend
dotnet ef migrations add InitialCreate --project Booking.Infrastructure --startup-project Booking.Host
```

Expected: `Migration 'InitialCreate' added.` (If `dotnet ef` missing: `dotnet tool install --global dotnet-ef`.)

- [ ] **Step 8: Build + commit**

Run: `dotnet build`
Expected: `Build succeeded`

```bash
git add backend/ && git commit -m "feat(infra): sqlite dbcontext, seeding, fixed meet link, r2 backup worker"
```

---

### Task 5: Auth — magic codes + cookies + endpoints

**Files:**
- Create: `backend/Booking.Application/Auth/RequestCodeCommand.cs` + handler, `VerifyCodeCommand.cs` + handler, `LogoutCommand.cs` + handler, `GetMeQuery.cs` + handler
- Create: `backend/Booking.Application/Abstractions/ICodeSender.cs`
- Create: `backend/Booking.Infrastructure/Auth/EmailCodeSender.cs`, `Auth/CodeGenerator.cs`
- Create: `backend/Booking.Endpoints/AuthEndpoints.cs`
- Test: `backend/Booking.Tests/Auth/AuthFlowTests.cs`

- [ ] **Step 1: Application auth pieces**

`backend/Booking.Application/Abstractions/ICodeSender.cs`:

```csharp
namespace Booking.Application.Abstractions;

public interface ICodeSender
{
    Task SendAsync(string email, string name, string code, CancellationToken ct = default);
}
```

`backend/Booking.Application/Auth/RequestCodeCommand.cs`:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Application.Auth;

public sealed record RequestCodeCommand(string Email) : ICommand<bool>;
```

`backend/Booking.Application/Auth/RequestCodeCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Auth;

public sealed class RequestCodeCommandHandler(IAppDbContext db, ICodeSender sender)
    : ICommandHandler<RequestCodeCommand, bool>
{
    public async Task<bool> Handle(RequestCodeCommand c, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email.Trim().ToLower(), ct);
        if (user is null) return true; // do not reveal registered emails

        var code = CodeGenerator.Generate6();
        db.AuthCodes.Add(new AuthCode
        {
            UserId = user.Id,
            CodeHash = CodeGenerator.Hash(code),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync(ct);
        await sender.SendAsync(user.Email, user.Name, code, ct);
        return true;
    }
}
```

`CodeGenerator` lives in Application (pure, testable) — `backend/Booking.Application/Auth/CodeGenerator.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Booking.Application.Auth;

public static class CodeGenerator
{
    public static string Generate6()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000;
        return value.ToString("D6");
    }

    public static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
```

`backend/Booking.Application/Auth/VerifyCodeCommand.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed record VerifyCodeCommand(string Email, string Code) : ICommand<UserDto>;
```

`backend/Booking.Application/Auth/VerifyCodeCommandHandler.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Auth;

public sealed class VerifyCodeCommandHandler(IAppDbContext db)
    : ICommandHandler<VerifyCodeCommand, UserDto>
{
    public async Task<UserDto> Handle(VerifyCodeCommand c, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email.Trim().ToLower(), ct)
                   ?? throw new UnauthorizedAccessException("Invalid code.");

        var hash = CodeGenerator.Hash(c.Code.Trim());
        var match = await db.AuthCodes
            .Where(a => a.UserId == user.Id && a.CodeHash == hash && a.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        if (match.Count == 0) throw new UnauthorizedAccessException("Invalid code.");

        db.AuthCodes.RemoveRange(match); // single-use
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Name, user.Email, user.Role.ToString());
    }
}
```

`backend/Booking.Application/Auth/LogoutCommand.cs` — not needed; cookie sign-out is done directly in the endpoint (below).

`backend/Booking.Application/Auth/GetMeQuery.cs` + handler:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed record GetMeQuery : IQuery<UserDto?>;

public sealed class GetMeQueryHandler(ICurrentUser user) : IQueryHandler<GetMeQuery, UserDto?>
{
    public Task<UserDto?> Handle(GetMeQuery q, CancellationToken ct) =>
        Task.FromResult<UserDto?>(new UserDto(user.UserId, user.Name, "", user.IsAdmin ? "Admin" : "Student"));
}
```

- [ ] **Step 2: Email sender (Infrastructure)**

`backend/Booking.Infrastructure/Auth/EmailCodeSender.cs`:

```csharp
using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Auth;

public sealed class EmailCodeSender(IConfiguration config, ILogger<EmailCodeSender> log) : ICodeSender
{
    public async Task SendAsync(string email, string name, string code, CancellationToken ct = default)
    {
        var smtp = config.GetSection("Smtp");
        var host = smtp["Host"];
        if (string.IsNullOrEmpty(host))
        {
            log.LogWarning("SMTP not configured — code for {Email}: {Code}", email, code);
            return;
        }
        using var msg = new System.Net.Mail.MailMessage(
            from: smtp["From"] ?? "bookings@example.com",
            to: email,
            subject: "Your booking login code",
            body: $"Hi {name}, your login code is {code}. It expires in 10 minutes.");
        using var client = new System.Net.Mail.SmtpClient(host, int.Parse(smtp["Port"] ?? "587"));
        await client.SendMailAsync(msg, ct);
    }
}
```

Register in `DependencyInjection.cs` (Infrastructure): `services.AddScoped<ICodeSender, EmailCodeSender>();`

- [ ] **Step 3: Auth endpoints**

`backend/Booking.Endpoints/AuthEndpoints.cs`:

```csharp
using System.Security.Claims;
using Booking.Application.Abstractions;
using Booking.Application.Auth;
using Booking.Application.DTOs;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Booking.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/request-code", async (RequestCodeRequest req, ISender sender) =>
        {
            await sender.Send(new RequestCodeCommand(req.Email));
            return Results.Ok(new { sent = true });
        });

        group.MapPost("/verify", async (VerifyCodeRequest req, ISender sender, HttpContext http) =>
        {
            var user = await sender.Send(new VerifyCodeCommand(req.Email, req.Code));
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
            ], "cookies");
            if (user.Role == "Admin") identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
            return Results.Ok(user);
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/me", async (HttpContext http, ISender sender) =>
        {
            if (!http.User.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
            var user = await sender.Send(new GetMeQuery());
            return Results.Ok(user);
        }).RequireAuthorization();

        return app;
    }

    public sealed record RequestCodeRequest(string Email);
    public sealed record VerifyCodeRequest(string Email, string Code);
}
```

- [ ] **Step 4: Wire Program.cs (auth + all endpoints so far)**

Replace `backend/Host/Program.cs`:

```csharp
using Booking.Application;
using Booking.Endpoints;
using Booking.Infrastructure;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();

await app.MigrateAndSeedAsync();
app.Run();

public partial class Program { }
```

`backend/Host/appsettings.json` (replace template content):

```json
{
  "App": {
    "DbPath": "data/booking.db",
    "FixedMeetLink": "https://meet.google.com/CHANGE-ME",
    "Users": [
      { "Name": "Teacher", "Email": "teacher@example.com", "Phone": null, "Role": "Admin", "TimeZoneId": "Africa/Harare" },
      { "Name": "Student A", "Email": "studenta@example.com", "Phone": null, "Role": "Student" },
      { "Name": "Student B", "Email": "studentb@example.com", "Phone": null, "Role": "Student" }
    ]
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

(Real emails go in environment-specific config/user-secrets, never committed — the commit only contains the placeholders above + README note. Replace emails at deploy time via env vars `App__Users__0__Email` etc.)

- [ ] **Step 5: Write auth flow test**

`backend/Booking.Tests/Auth/AuthFlowTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Tests.Auth;

public class AuthFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_flow_request_verify_me_logout()
    {
        var client = _factory.CreateClient();

        var req = await client.PostAsJsonAsync("/api/auth/request-code", new { email = "studenta@example.com" });
        req.EnsureSuccessStatusCode();

        var code = ApiFactory.LastCode; // captured by fake ICodeSender
        Assert.Matches(@"^\d{6}$", code);

        var verify = await client.PostAsJsonAsync("/api/auth/verify",
            new { email = "studenta@example.com", code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var me = await client.GetFromJsonAsync<UserMe>("/api/auth/me");
        Assert.Equal("Student A", me!.Name);

        await client.PostAsync("/api/auth/logout", null);
        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    public sealed record UserMe(Guid Id, string Name, string Email, string Role);
}
```

Shared factory — `backend/Booking.Tests/ApiFactory.cs`:

```csharp
using Booking.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booking.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public static string LastCode { get; private set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            // fake code sender records the code for tests
            services.RemoveAll<ICodeSender>();
            services.AddSingleton<ICodeSender>(new FakeSender());
            // fresh SQLite file per test-host run
            var path = Path.Combine(Path.GetTempPath(), $"tests-{Guid.NewGuid():N}.db");
            services.RemoveAll<SqliteConnection>();
            services.AddSingleton(new SqliteConnection($"Data Source={path}"));
        });
    }

    private sealed class FakeSender : ICodeSender
    {
        public Task SendAsync(string email, string name, string code, CancellationToken ct = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}
```

The seeding runs in `Program.cs` startup (`MigrateAndSeedAsync`) against the swapped SQLite connection, so seed users exist in tests. Because `LastCode` is only set *after* a request, the auth test must POST `/api/auth/request-code` first, then read `ApiFactory.LastCode`:

```csharp
var req = await client.PostAsJsonAsync("/api/auth/request-code", new { email = "studenta@example.com" });
req.EnsureSuccessStatusCode();
var code = ApiFactory.LastCode;
Assert.Matches(@"^\d{6}$", code);
```

- [ ] **Step 6: Run tests**

Run: `dotnet test` (in `backend/`)
Expected: domain tests + auth flow pass.

- [ ] **Step 7: Commit**

```bash
git add backend/ && git commit -m "feat(auth): magic-code login with cookie sessions"
```

---

### Task 6: Booking + admin endpoints + ICS (integration-tested)

**Files:**
- Create: `backend/Booking.Endpoints/SlotEndpoints.cs`, `BookingEndpoints.cs`, `AdminEndpoints.cs`
- Test: `backend/Booking.Tests/Api/BookingApiTests.cs`

- [ ] **Step 1: Write failing API tests**

`backend/Booking.Tests/Api/BookingApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Booking.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Booking.Tests.Api;

public class BookingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public BookingApiTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/request-code", new { email });
        var res = await client.PostAsJsonAsync("/api/auth/verify", new { email, code = ApiFactory.LastCode });
        res.EnsureSuccessStatusCode();
        return client;
    }

    private static DateOnly FutureThursday(int weeksAhead = 3)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(7 * weeksAhead);
        while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task Student_books_then_sees_it_on_shared_calendar()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday();

        var booking = await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);

        var days = await student.GetFromJsonAsync<List<SlotDayDto>>($"/api/slots?month={date:yyyy-MM}");
        var day = days!.Single(d => d.Date == date);
        Assert.Equal(DayState.Booked, day.State);
        Assert.Contains("Student A", day.StudentNames);
    }

    [Fact]
    public async Task Second_student_same_day_becomes_combined()
    {
        var a = await LoginAsync("studenta@example.com");
        var b = await LoginAsync("studentb@example.com");
        var date = FutureThursday(4);

        await a.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });
        await b.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });

        var days = await b.GetFromJsonAsync<List<SlotDayDto>>($"/api/slots?month={date:yyyy-MM}");
        Assert.Equal(DayState.Combined, days!.Single(d => d.Date == date).State);
    }

    [Fact]
    public async Task Sunday_rejected_and_blocked_day_rejected()
    {
        var student = await LoginAsync("studenta@example.com");
        var sunday = FutureThursday(); while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);

        var res = await student.PostAsJsonAsync("/api/bookings", new { date = sunday.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Sunday", await res.Content.ReadAsStringAsync());

        var admin = await LoginAsync("teacher@example.com");
        var thursday = FutureThursday(5);
        await admin.PostAsJsonAsync("/api/admin/blocked-days", new { date = thursday.ToString("yyyy-MM-dd"), reason = "trip" });

        var blocked = await student.PostAsJsonAsync("/api/bookings", new { date = thursday.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }

    [Fact]
    public async Task Cancel_and_reschedule_own_booking()
    {
        var student = await LoginAsync("studentb@example.com");
        var date = FutureThursday(6);
        var other = FutureThursday(7);

        var created = await (await student.PostAsJsonAsync("/api/bookings",
            new { date = date.ToString("yyyy-MM-dd") })).Content.ReadFromJsonAsync<BookingDto>();

        var moved = await (await student.PostAsJsonAsync($"/api/bookings/{created!.Id}/reschedule",
            new { newDate = other.ToString("yyyy-MM-dd") })).Content.ReadFromJsonAsync<BookingDto>();
        Assert.Equal(other, moved!.Date);
        Assert.Equal(date, moved.OriginalDate);

        var cancel = await student.DeleteAsync($"/api/bookings/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var mine = await student.GetFromJsonAsync<List<BookingDto>>("/api/bookings/mine");
        Assert.DoesNotContain(mine!, b => b.Id == created.Id);
    }

    [Fact]
    public async Task Anonymous_cannot_book_and_student_cannot_block()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/slots?month=2026-10")).StatusCode);

        var student = await LoginAsync("studenta@example.com");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await student.PostAsJsonAsync("/api/admin/blocked-days",
                new { date = "2026-10-15", reason = "nope" })).StatusCode);
    }

    [Fact]
    public async Task Ics_downloads_with_meet_link_and_harare_tz()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday(8);
        await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });

        var ics = await student.GetStringAsync($"/api/slots/{date:yyyy-MM-dd}/ics");
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("TZID=Africa/Harare", ics);
        Assert.Contains("meet.google.com", ics);
    }
}
```

- [ ] **Step 2: Run, verify compile errors (endpoints missing)**

Run: `dotnet test --filter BookingApiTests`
Expected: compile errors.

- [ ] **Step 3: Implement endpoints**

`backend/Booking.Endpoints/SlotEndpoints.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.Slots;

namespace Booking.Endpoints;

public static class SlotEndpoints
{
    public static IEndpointRouteBuilder MapSlots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots", async (int year, int month, ISender sender) =>
            Results.Ok(await sender.Send(new GetMonthQuery(year, month))))
           .RequireAuthorization();
        return app;
    }
}
```

The ICS route goes in its own file — `backend/Booking.Endpoints/IcsEndpoint.cs`:

`backend/Booking.Endpoints/IcsEndpoint.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Domain.Slots;

namespace Booking.Endpoints;

public static class IcsEndpoint
{
    public static IEndpointRouteBuilder MapIcs(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots/{date}/ics", async (DateOnly date, ISender sender) =>
        {
            var days = await sender.Send(new GetMonthQuery(date.Year, date.Month));
            var day = days.FirstOrDefault(d => d.Date == date);
            if (day?.MeetLink is null) return Results.NotFound();

            var start = date.ToString("yyyyMMdd") + "T203000";
            var end = date.ToString("yyyyMMdd") + "T223000";
            var ics = string.Join("\r\n",
                "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//ClassBooking//EN",
                "BEGIN:VTIMEZONE", "TZID:Africa/Harare", "BEGIN:STANDARD",
                "DTSTART:19700101T000000", "TZOFFSETFROM:+0200", "TZOFFSETTO:+0200",
                "TZNAME:CAT", "END:STANDARD", "END:VTIMEZONE",
                "BEGIN:VEVENT",
                $"UID:lesson-{date:yyyyMMdd}@booking",
                $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
                $"DTSTART;TZID=Africa/Harare:{start}",
                $"DTEND;TZID=Africa/Harare:{end}",
                "SUMMARY:Programming lesson",
                $"LOCATION:{day.MeetLink}",
                $"DESCRIPTION:Lesson with the crew. Join: {day.MeetLink}",
                "END:VEVENT", "END:VCALENDAR");

            return Results.Text(ics, "text/calendar", System.Text.Encoding.UTF8);
        }).RequireAuthorization();
        return app;
    }
}
```

`backend/Booking.Endpoints/BookingEndpoints.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.Bookings;
using Booking.Application.DTOs;
using Booking.Application.Slots;

namespace Booking.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").RequireAuthorization();

        group.MapGet("/mine", async (ISender sender) =>
            Results.Ok(await sender.Send(new GetMyBookingsQuery())));

        group.MapPost("/", async (CreateBookingRequest req, ISender sender) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new CreateBookingCommand(req.Date)));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleRequest req, ISender sender) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new RescheduleBookingCommand(id, req.NewDate)));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            try { return Results.Ok(await sender.Send(new CancelBookingCommand(id))); }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }

    public sealed record CreateBookingRequest(DateOnly Date);
    public sealed record RescheduleRequest(DateOnly NewDate);
}
```

`backend/Booking.Endpoints/AdminEndpoints.cs`:

```csharp
using Booking.Application.Abstractions;
using Booking.Application.BlockedDays;
using Booking.Infrastructure.Backups;

namespace Booking.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("Admin"));

        group.MapPost("/blocked-days", async (BlockRequest req, ISender sender) =>
        {
            await sender.Send(new BlockDayCommand(req.Date, req.Reason));
            return Results.Ok(new { ok = true });
        });

        group.MapDelete("/blocked-days/{date}", async (DateOnly date, ISender sender) =>
        {
            await sender.Send(new UnblockDayCommand(date));
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/backups", async (IBackupService backups) =>
            Results.Ok(new { lastBackupUtc = await backups.LastBackupUtcAsync() }));

        group.MapPost("/backups/restore", async (IBackupService backups) =>
        {
            await backups.RestoreLatestAsync();
            return Results.Ok(new { restoring = true });
        });

        return app;
    }

    public sealed record BlockRequest(DateOnly Date, string? Reason);
}
```

Program.cs additions (after `app.MapAuth();`):

```csharp
app.MapSlots();
app.MapIcs();
app.MapBookings();
app.MapAdmin();
```

Add `using Booking.Application.Bookings;` to `SlotEndpoints.cs`? Not needed — remove unused usings so build is clean. Add a global exception mapping for `UnauthorizedAccessException` → 403 in Program.cs:

```csharp
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 403; }
});
```

(Place before auth middleware. This makes the student-cannot-block test return 403.)

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test`
Expected: all pass (domain + auth + API). If unique-index violations occur on repeat bookings in the same factory, that's the policy working — verify tests use distinct dates (they do: weeksAhead 3–8).

- [ ] **Step 5: Commit**

```bash
git add backend/ && git commit -m "feat(api): slots/bookings/admin endpoints, ics, integration tests"
```

---

### Task 7: Frontend scaffold + login + shell

**Files:**
- Create: `frontend/` Vite Vue3-TS app (`package.json`, `vite.config.ts`, `index.html`, `src/main.ts`, `src/App.vue`, `src/router.ts`, `src/vite-env.d.ts`)
- Create: `src/composables/useApi.ts`, `src/composables/useAuth.ts`, `src/composables/useTime.ts`
- Create: `src/views/LoginView.vue`, `src/views/HomeView.vue`, `src/views/PrivacyView.vue`, `src/views/TermsView.vue`
- Test: `src/composables/useTime.test.ts`

Note: load the `nuxt-ui` skill before starting this task to confirm exact @nuxt/ui v4 Vite setup.

- [ ] **Step 1: Scaffold**

```bash
npm create vue@latest frontend -- --ts --router --vitest
cd frontend && npm install @nuxt/ui
```

(If the interactive scaffold asks questions, choose: TypeScript yes, Router yes, Vitest yes, everything else no.)

`frontend/vite.config.ts`:

```ts
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import ui from '@nuxt/ui/vite'

export default defineConfig({
  plugins: [vue(), ui()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: { proxy: { '/api': 'http://localhost:8080' } },
})
```

- [ ] **Step 2: Composables**

`src/composables/useApi.ts`:

```ts
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`/api${path}`, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  })
  if (res.status === 401) throw new UnauthorizedError()
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }))
    throw new ApiError(body.error ?? 'Request failed')
  }
  return res.json() as Promise<T>
}

export class UnauthorizedError extends Error { constructor() { super('unauthorized') } }
export class ApiError extends Error {}
```

`src/composables/useAuth.ts`:

```ts
import { ref } from 'vue'

export interface Me { id: string; name: string; role: 'Admin' | 'Student' }

export const user = ref<Me | null>(null)

export async function loadMe() {
  try { user.value = await api<Me>('/auth/me') }
  catch { user.value = null }
}

export async function requestCode(email: string) {
  await api('/auth/request-code', { method: 'POST', body: JSON.stringify({ email }) })
}

export async function verifyCode(email: string, code: string) {
  user.value = await api<Me>('/auth/verify', { method: 'POST', body: JSON.stringify({ email, code }) })
}

export async function logout() {
  await api('/auth/logout', { method: 'POST' })
  user.value = null
}
```

(fix import: `import { api } from './useApi'`)

`src/composables/useTime.ts`:

```ts
export interface SlotDay {
  date: string
  startUtc: string
  state: 'Bookable' | 'Booked' | 'Combined' | 'Sunday' | 'Blocked' | 'Past' | 'Cutoff'
  canBook: boolean
  reason: string | null
  studentNames: string[]
  meetLink: string | null
}

export function formatLocal(utcIso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    hour: '2-digit', minute: '2-digit', timeZone: timeZone ?? undefined,
  }).format(new Date(utcIso))
}

export function formatDayLocal(utcIso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    weekday: 'short', day: 'numeric', month: 'short',
    hour: '2-digit', minute: '2-digit', timeZone: timeZone ?? undefined,
  }).format(new Date(utcIso))
}

/** "19:30 your time · 20:30 Harare" — viewer zone automatic via Intl. */
export function dualTimeLabel(utcIso: string): string {
  return `${formatLocal(utcIso)} your time · ${formatLocal(utcIso, 'Africa/Harare')} Harare`
}
```

- [ ] **Step 3: Write useTime test (TDD the TZ edge)**

`src/composables/useTime.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { formatLocal, dualTimeLabel } from './useTime'

describe('useTime', () => {
  it('shows viewer-zone time — BST example', () => {
    // 2026-09-22 18:30Z == 19:30 London (BST), 20:30 Harare
    expect(formatLocal('2026-09-22T18:30:00Z', 'Europe/London')).toBe('19:30')
    expect(formatLocal('2026-09-22T18:30:00Z', 'Africa/Harare')).toBe('20:30')
  })
  it('winter — GMT example', () => {
    // 2026-12-03 18:30Z == 18:30 London (GMT), still 20:30 Harare
    expect(formatLocal('2026-12-03T18:30:00Z', 'Europe/London')).toBe('18:30')
  })
  it('dual label contains both zones', () => {
    const label = dualTimeLabel('2026-09-22T18:30:00Z')
    expect(label).toContain('20:30 Harare')
    expect(label).toContain('your time')
  })
})
```

Run: `cd frontend && npx vitest run src/composables/useTime.test.ts`
Expected: 3 passed (Intl makes this deterministic since we pass explicit zones).

- [ ] **Step 4: Router, App shell, views**

`src/router.ts`:

```ts
import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('./views/HomeView.vue') },
    { path: '/login', component: () => import('./views/LoginView.vue') },
    { path: '/calendar', component: () => import('./views/CalendarView.vue'), meta: { auth: true } },
    { path: '/mine', component: () => import('./views/MyLessonsView.vue'), meta: { auth: true } },
    { path: '/admin', component: () => import('./views/AdminView.vue'), meta: { auth: true, admin: true } },
    { path: '/privacy', component: () => import('./views/PrivacyView.vue') },
    { path: '/terms', component: () => import('./views/TermsView.vue') },
  ],
})

router.beforeEach((to) => {
  if (to.meta.auth && !user.value) return '/login'
  if (to.meta.admin && user.value?.role !== 'Admin') return '/calendar'
})

export default router
```

(add `import { user } from './composables/useAuth'`; declare module meta via `declare module 'vue-router' { interface RouteMeta { auth?: boolean; admin?: boolean } }`)

`src/main.ts`:

```ts
import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import { loadMe } from './composables/useAuth'
import './main.css'

loadMe().finally(() => {
  const app = createApp(App)
  app.use(router)
  app.mount('#app')
})
```

`src/main.css` (Nuxt UI needs tailwind via its vite plugin — @nuxt/ui/vite handles CSS; add only page basics):

```css
body { @apply bg-gray-50 text-gray-900; }
```

(If `@apply` needs Tailwind directives: @nuxt/ui/vite injects them; if build errors, replace with plain `body { background:#f9fafb; color:#111827 }`.)

`src/App.vue`:

```vue
<script setup lang="ts">
import { user, logout } from './composables/useAuth'
</script>

<template>
  <div class="min-h-screen">
    <header class="border-b bg-white">
      <nav class="mx-auto flex max-w-4xl items-center gap-4 p-4">
        <RouterLink to="/calendar" class="font-bold">Lesson Booking</RouterLink>
        <template v-if="user">
          <RouterLink to="/calendar">Calendar</RouterLink>
          <RouterLink to="/mine">My Lessons</RouterLink>
          <RouterLink v-if="user.role === 'Admin'" to="/admin">Admin</RouterLink>
          <span class="ml-auto text-sm text-gray-500">{{ user.name }}</span>
          <UButton variant="ghost" @click="logout()">Log out</UButton>
        </template>
        <UButton v-else class="ml-auto" to="/login">Log in</UButton>
      </nav>
    </header>
    <main class="mx-auto max-w-4xl p-4"><RouterView /></main>
    <footer class="mx-auto max-w-4xl p-4 text-sm text-gray-400">
      <RouterLink to="/privacy">Privacy</RouterLink> ·
      <RouterLink to="/terms">Terms</RouterLink>
    </footer>
  </div>
</template>
```

`src/views/LoginView.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { requestCode, verifyCode } from '../composables/useAuth'

const email = ref('')
const code = ref('')
const sent = ref(false)
const error = ref('')
const busy = ref(false)
const router = useRouter()

async function step1() {
  busy.value = true; error.value = ''
  try { await requestCode(email.value); sent.value = true }
  catch (e: any) { error.value = e.message }
  finally { busy.value = false }
}

async function step2() {
  busy.value = true; error.value = ''
  try { await verifyCode(email.value, code.value); router.push('/calendar') }
  catch (e: any) { error.value = 'Invalid or expired code' }
  finally { busy.value = false }
}
</script>

<template>
  <UCard class="mx-auto max-w-sm">
    <h1 class="mb-4 text-xl font-bold">Log in</h1>
    <UForm v-if="!sent" @submit="step1">
      <UInput v-model="email" type="email" placeholder="Your email" required class="mb-3 w-full" />
      <UButton type="submit" :loading="busy" block>Send code</UButton>
    </UForm>
    <UForm v-else @submit="step2">
      <p class="mb-3 text-sm text-gray-500">Code sent to {{ email }} (check WhatsApp/email).</p>
      <UInput v-model="code" inputmode="numeric" maxlength="6" placeholder="6-digit code" required class="mb-3 w-full" />
      <UButton type="submit" :loading="busy" block>Verify</UButton>
    </UForm>
    <p v-if="error" class="mt-3 text-sm text-red-500">{{ error }}</p>
  </UCard>
</template>
```

`src/views/HomeView.vue`:

```vue
<template>
  <div class="py-16 text-center">
    <h1 class="mb-3 text-4xl font-bold">Evening Programming Lessons</h1>
    <p class="mb-8 text-gray-500">Daily 20:30 Harare · 2h · book when you're free, see when the crew is in.</p>
    <UButton to="/login" size="xl">Get started</UButton>
  </div>
</template>
```

`src/views/PrivacyView.vue` + `src/views/TermsView.vue` (plain static; privacy names data: email, WhatsApp number, booking dates; no third-party sharing; contact = teacher email; terms: booking/cancel policy):

```vue
<!-- PrivacyView.vue -->
<template>
  <article class="prose">
    <h1>Privacy Policy</h1>
    <p>We store your email address, WhatsApp number and lesson bookings. Data is used only to run lessons and send reminders. It is never sold or shared. Backups are encrypted at rest in Cloudflare R2.</p>
    <p>Contact: teacher@example.com</p>
  </article>
</template>
```

```vue
<!-- TermsView.vue -->
<template>
  <article class="prose">
    <h1>Terms</h1>
    <p>Lessons run 20:30–22:30 Africa/Harare, Monday–Saturday. Book freely, cancel or reschedule any time. Bookings close 30 minutes before start.</p>
  </article>
</template>
```

(Replace `teacher@example.com` with the real address in Task 12 deploy config — it's public info, fine to commit.)

- [ ] **Step 5: Placeholder stubs for the 3 authed views so router resolves**

Create minimal `CalendarView.vue`, `MyLessonsView.vue`, `AdminView.vue`, each:

```vue
<template><p>Coming in next task.</p></template>
```

- [ ] **Step 6: Verify dev build + tests**

Run: `cd frontend && npm run test:unit -- --run && npm run build`
Expected: unit tests pass, build succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/ && git commit -m "feat(frontend): scaffold, auth flow, shell, static compliance pages"
```

---

### Task 8: Calendar + My Lessons + Admin views

**Files:**
- Modify: `frontend/src/views/CalendarView.vue`, `frontend/src/views/MyLessonsView.vue`, `frontend/src/views/AdminView.vue` (replace stubs)
- Create: `frontend/src/components/BookingModal.vue`

- [ ] **Step 1: CalendarView**

```vue
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import { dualTimeLabel, formatDayLocal, type SlotDay } from '../composables/useTime'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const error = ref('')
const booking = ref<SlotDay | null>(null)
const bookingError = ref('')

const stateClass: Record<SlotDay['state'], string> = {
  Bookable: 'bg-green-100 hover:bg-green-200 cursor-pointer',
  Booked: 'bg-blue-100',
  Combined: 'bg-purple-100',
  Sunday: 'bg-gray-100 text-gray-400',
  Blocked: 'bg-red-50 text-gray-400',
  Past: 'bg-gray-100 text-gray-300',
  Cutoff: 'bg-yellow-50 text-gray-400',
}

const grid = computed(() => {
  const first = new Date(year.value, month.value - 1, 1)
  const offset = (first.getDay() + 6) % 7 // Monday-first
  const cells: (SlotDay | null)[] = Array(offset).fill(null)
  return [...cells, ...days.value]
})

async function load() {
  error.value = ''
  try { days.value = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`) }
  catch (e: any) { error.value = e.message }
}

function shift(delta: number) {
  const d = new Date(year.value, month.value - 1 + delta, 1)
  year.value = d.getFullYear(); month.value = d.getMonth() + 1
  load()
}

function openBooking(day: SlotDay) { if (day.canBook) { booking.value = day; bookingError.value = '' } }

async function confirmBooking() {
  if (!booking.value) return
  try {
    await api('/bookings', { method: 'POST', body: JSON.stringify({ date: booking.value.date }) })
    booking.value = null
    await load()
  } catch (e: any) { bookingError.value = e instanceof ApiError ? e.message : 'Booking failed' }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="mb-4 flex items-center justify-between">
      <UButton icon="i-lucide-chevron-left" variant="ghost" @click="shift(-1)" />
      <h1 class="text-xl font-bold">{{ new Date(year, month - 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }) }}</h1>
      <UButton icon="i-lucide-chevron-right" variant="ghost" @click="shift(1)" />
    </div>
    <p v-if="error" class="text-red-500">{{ error }}</p>
    <div class="grid grid-cols-7 gap-1 text-center text-xs">
      <div v-for="d in ['Mon','Tue','Wed','Thu','Fri','Sat','Sun']" :key="d" class="p-2 font-semibold">{{ d }}</div>
      <template v-for="(day, i) in grid" :key="i">
        <div v-if="!day" />
        <div v-else class="min-h-20 rounded-lg p-2" :class="stateClass[day.state]" @click="openBooking(day)">
          <div class="font-bold">{{ day.date.slice(-2) }}</div>
          <div v-for="name in day.studentNames" :key="name" class="truncate text-[11px]">{{ name }}</div>
          <div v-if="day.state === 'Combined'" class="text-[11px] font-semibold">combined</div>
        </div>
      </template>
    </div>
    <BookingModal v-model:open="booking" :day="booking" @booked="load" @error="bookingError = $event" />

    <UCard v-if="!days.some(d => d.studentNames.length >= 2)" class="mt-6 text-sm text-gray-500">
      No combined lesson yet this month — the shared calendar keeps us honest. 😉
    </UCard>
  </div>
</template>
```

`frontend/src/components/BookingModal.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import { dualTimeLabel, type SlotDay } from '../composables/useTime'

const props = defineProps<{ day: SlotDay | null }>()
const emit = defineEmits<{ booked: []; error: [string] }>()
const busy = ref(false)

const open = defineModel<SlotDay | null>('open')

async function book() {
  if (!props.day) return
  busy.value = true
  try {
    await api('/bookings', { method: 'POST', body: JSON.stringify({ date: props.day.date }) })
    open.value = null
    emit('booked')
  } catch (e) {
    emit('error', e instanceof ApiError ? e.message : 'Booking failed')
  } finally { busy.value = false }
}
</script>

<template>
  <UModal :open="!!day" @update:open="open = null">
    <template #content>
      <div class="p-6">
        <h2 class="mb-2 text-lg font-bold">Book {{ day?.date }}</h2>
        <p class="mb-1 text-gray-600">{{ day ? formatDayLocal(day.startUtc) }}</p>
        <p class="mb-4 text-sm text-gray-500">{{ day ? dualTimeLabel(day.startUtc) }}</p>
        <div class="flex gap-2">
          <UButton :loading="busy" @click="book">Confirm booking</UButton>
          <UButton variant="soft" @click="open = null">Cancel</UButton>
        </div>
      </div>
    </template>
  </UModal>
</template>
```

(Remove the duplicate `bookingError`/`confirmBooking` logic from CalendarView since the modal owns the action — CalendarView keeps only `booking`, `openBooking`, and listening to `@booked`/`@error`.)

- [ ] **Step 2: MyLessonsView**

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import { formatDayLocal, type SlotDay } from '../composables/useTime'
import { user } from '../composables/useAuth'

interface MyBooking {
  id: string; date: string; startUtc: string; originalDate: string | null; meetLink: string | null
}
const bookings = ref<MyBooking[]>([])
const month = ref(new Date().getMonth() + 1)
const year = ref(new Date().getFullYear())
const days = ref<SlotDay[]>([])
const rescheduling = ref<MyBooking | null>(null)
const message = ref('')

async function load() { bookings.value = await api<MyBooking[]>('/bookings/mine') }

async function cancel(id: string) {
  await api(`/bookings/${id}`, { method: 'DELETE' })
  message.value = 'Cancelled.'
  await load()
}

async function loadSlotsForReschedule() {
  days.value = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`)
}

async function rescheduleTo(date: string) {
  if (!rescheduling.value) return
  try {
    await api(`/bookings/${rescheduling.value.id}/reschedule`, { method: 'POST', body: JSON.stringify({ newDate: date }) })
    rescheduling.value = null
    message.value = 'Moved!'
    await load()
  } catch { message.value = 'Could not move to that day.' }
}

onMounted(load)
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-bold">My lessons</h1>
    <p v-if="message" class="mb-3 text-sm text-green-600">{{ message }}</p>
    <UCard v-for="b in bookings" :key="b.id" class="mb-3">
      <div class="flex items-center justify-between">
        <div>
          <p class="font-semibold">{{ formatDayLocal(b.startUtc) }}</p>
          <p class="text-sm text-gray-500">
            moved from {{ b.originalDate }} · <a v-if="b.meetLink" :href="b.meetLink" class="underline">Meet link</a>
          </p>
        </div>
        <div class="flex gap-2">
          <UButton variant="soft" @click="rescheduling = b; loadSlotsForReschedule()">Reschedule</UButton>
          <UButton color="red" variant="soft" @click="cancel(b.id)">Cancel</UButton>
        </div>
      </div>
    </UCard>
    <p v-if="!bookings.length" class="text-gray-500">No upcoming lessons — the calendar is waiting.</p>

    <UModal :open="!!rescheduling" @update:open="rescheduling = null">
      <template #content>
        <div class="p-6">
          <h2 class="mb-3 font-bold">Move to which day?</h2>
          <div class="grid grid-cols-4 gap-2">
            <UButton v-for="d in days.filter(x => x.canBook)" :key="d.date" variant="outline" @click="rescheduleTo(d.date)">
              {{ d.date.slice(-2) }}
            </UButton>
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
```

- [ ] **Step 3: AdminView**

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import type { SlotDay } from '../composables/useTime'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const lastBackup = ref<string | null>(null)
const restoring = ref(false)

async function load() {
  days.value = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`)
}
async function loadBackup() {
  const res = await api<{ lastBackupUtc: string | null }>('/admin/backups')
  lastBackup.value = res.lastBackupUtc
}
async function toggle(day: SlotDay) {
  if (day.state === 'Blocked') await api(`/admin/blocked-days/${day.date}`, { method: 'DELETE' })
  else if (day.state !== 'Sunday' && day.state !== 'Past') await api('/admin/blocked-days', { method: 'POST', body: JSON.stringify({ date: day.date, reason: 'unavailable' }) })
  await load()
}
async function restore() {
  if (!confirm('Restore latest R2 backup? The app will restart.')) return
  restoring.value = true
  await api('/admin/backups/restore', { method: 'POST' })
}

onMounted(() => { load(); loadBackup() })
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-bold">Admin — block days</h1>
    <div class="mb-4 flex items-center justify-between">
      <UButton variant="ghost" @click="month--; if (month === 0) { month = 12; year-- }; load()">‹</UButton>
      <span class="font-semibold">{{ year }}-{{ month }}</span>
      <UButton variant="ghost" @click="month++; if (month === 13) { month = 1; year++ }; load()">›</UButton>
    </div>
    <div class="grid grid-cols-7 gap-1 text-center">
      <div v-for="day in days" :key="day.date"
           class="min-h-16 rounded p-2 text-sm"
           :class="day.state === 'Blocked' ? 'bg-red-100' : day.state === 'Sunday' || day.state === 'Past' ? 'bg-gray-100 text-gray-400' : 'bg-green-50 cursor-pointer hover:bg-green-100'"
           @click="toggle(day)">
        {{ day.date.slice(-2) }}
      </div>
    </div>
    <UCard class="mt-6">
      <p class="text-sm">Last R2 backup: {{ lastBackup ? new Date(lastBackup).toLocaleString() : 'none yet' }}</p>
      <UButton class="mt-2" color="red" variant="soft" :loading="restoring" @click="restore">Restore latest backup</UButton>
    </UCard>
  </div>
</template>
```

- [ ] **Step 4: Verify build**

Run: `cd frontend && npm run build && npm run test:unit -- --run`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add frontend/ && git commit -m "feat(frontend): calendar with fomo states, my lessons, admin blocking"
```

---

### Task 9: Playwright E2E

**Files:**
- Create: `e2e/package.json`, `e2e/playwright.config.ts`, `e2e/tests/booking.spec.ts`
- Modify: `backend/Host/Program.cs` (E2E test-only code endpoint)

- [ ] **Step 1: Backend test hook (E2E environment only)**

`backend/Booking.Infrastructure/Auth/E2eCodeSender.cs` — a code sender that records the code in a static, plus the endpoint to read it. Both active only in the E2E environment:

```csharp
using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Auth;

public static class E2eCodeStore
{
    public static string Last = "";
}

public sealed class E2eCodeSender : ICodeSender
{
    public Task SendAsync(string email, string name, string code, CancellationToken ct = default)
    {
        E2eCodeStore.Last = code;
        return Task.CompletedTask;
    }
}
```

In `Program.cs`, after `app.MapAdmin();`:

```csharp
if (app.Environment.EnvironmentName == "E2E")
{
    app.MapGet("/api/test/latest-code/{email}", (string email) =>
        Results.Json(new { code = Booking.Infrastructure.Auth.E2eCodeStore.Last }));
}
```

In `AddInfrastructure` add at the end (requires `using Booking.Infrastructure.Auth; using Microsoft.Extensions.DependencyInjection.Extensions;`):

```csharp
if (config["E2E"] == "true")
    services.Replace(ServiceDescriptor.Scoped<ICodeSender, E2eCodeSender>());
```

E2E run: `E2E=true ASPNETCORE_ENVIRONMENT=E2E App__DbPath=data/e2e.db` and delete `data/e2e.db` before each run.

- [ ] **Step 2: Playwright scaffold**

`e2e/package.json`:

```json
{
  "name": "class-booking-e2e",
  "private": true,
  "scripts": { "test": "playwright test" },
  "devDependencies": {
    "@playwright/test": "^1.50.0"
  }
}
```

`e2e/playwright.config.ts`:

```ts
import { defineConfig } from '@playwright/test'
import { rmSync } from 'node:fs'

export default defineConfig({
  testDir: 'tests',
  timeout: 30_000,
  use: { baseURL: 'http://localhost:5173' },
  webServer: [
    {
      command: 'rm -f ../backend/data/e2e.db && cd ../backend && E2E=true ASPNETCORE_ENVIRONMENT=E2E ASPNETCORE_URLS=http://localhost:8080 dotnet run --project Booking.Host',
      url: 'http://localhost:8080/health',
      reuseExistingServer: true,
      timeout: 60_000,
    },
    {
      command: 'cd ../frontend && npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
})
```

- [ ] **Step 3: Journeys**

`e2e/tests/booking.spec.ts`:

```ts
import { expect, test } from '@playwright/test'

async function login(page: import('@playwright/test').Page, email: string) {
  await page.goto('/login')
  await page.getByPlaceholder('Your email').fill(email)
  await page.getByRole('button', { name: 'Send code' }).click()
  await page.getByPlaceholder('6-digit code').waitFor()
  const res = await page.request.get('http://localhost:8080/api/test/latest-code/' + email)
  const { code } = await res.json()
  await page.getByPlaceholder('6-digit code').fill(code)
  await page.getByRole('button', { name: 'Verify' }).click()
  await page.waitForURL('**/calendar')
}

async function futureThursday(weeksAhead: number): Promise<string> {
  const d = new Date()
  d.setDate(d.getDate() + weeksAhead * 7)
  while (d.getDay() !== 4) d.setDate(d.getDate() + 1)
  return d.toISOString().slice(0, 10)
}

test('student logs in, books, sees avatar on shared calendar', async ({ page }) => {
  await login(page, 'studenta@example.com')
  const date = await futureThursday(3)
  const cell = page.locator(`[data-date="${date}"]`)
  await cell.click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(cell).toContainText('Student A')
})

test('second student same day shows combined', async ({ page }) => {
  await login(page, 'studenta@example.com')
  const date = await futureThursday(4)
  await page.locator(`[data-date="${date}"]`).click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()

  const page2 = await page.context().browser()!.newPage()
  await login(page2, 'studentb@example.com')
  const cell2 = page2.locator(`[data-date="${date}"]`)
  await cell2.click()
  await page2.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(cell2).toContainText('combined')
})

test('cancel and reschedule from my lessons', async ({ page }) => {
  await login(page, 'studentb@example.com')
  const date = await futureThursday(5)
  await page.locator(`[data-date="${date}"]`).click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()

  await page.goto('/mine')
  const card = page.locator('div.flex.items-center', { hasText: date }).first()
  await card.getByRole('button', { name: 'Reschedule' }).click()
  const other = await futureThursday(6)
  await page.locator('button', { hasText: other.slice(-2) }).first().click()
  await expect(page.getByText('Moved!')).toBeVisible()
  await page.reload()
  await expect(page.getByText('moved from')).toBeVisible()
})

test('sunday and blocked days are not bookable, admin can block', async ({ page }) => {
  await login(page, 'teacher@example.com')
  const date = await futureThursday(6)
  await page.goto('/admin')
  await page.locator(`[data-date="${date}"]`).click()
  await expect(page.locator(`[data-date="${date}"]`)).toHaveClass(/bg-red/)

  const page2 = await page.context().browser()!.newPage()
  await login(page2, 'studenta@example.com')
  const cell = page2.locator(`[data-date="${date}"]`)
  await expect(cell).not.toHaveClass(/cursor-pointer/)
})
```

Add `data-date` attributes: in CalendarView day div add `:data-date="day.date"`, same in AdminView. (Modify both components in this task.)

- [ ] **Step 4: Run E2E locally**

```bash
cd e2e && npm install && npx playwright install --with-deps && npm test
```

Expected: 4 passed. Debug failures with `npx playwright test --debug` before proceeding.

- [ ] **Step 5: Commit**

```bash
git add e2e/ backend/ frontend/ && git commit -m "test: playwright e2e journeys"
```

---

### Task 10: Docker images + compose

**Files:**
- Create: `backend/Dockerfile`, `frontend/Dockerfile`, `frontend/nginx.conf`, `infra/docker-compose.yml`

- [ ] **Step 1: Backend Dockerfile**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Booking.Host -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
VOLUME /app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Booking.Host.dll"]
```

- [ ] **Step 2: Frontend Dockerfile + nginx**

```dockerfile
FROM node:22 AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
```

`frontend/nginx.conf`:

```nginx
server {
    listen 80;
    root /usr/share/nginx/html;
    index index.html;
    location /api/ {
        proxy_pass http://backend:8080;
        proxy_set_header Host $host;
    }
    location / { try_files $uri $uri/ /index.html; }
}
```

- [ ] **Step 3: Compose (infra/)**

`infra/docker-compose.yml`:

```yaml
services:
  backend:
    image: ghcr.io/${GH_OWNER}/class-booking-backend:${TAG:-latest}
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - App__DbPath=/app/data/booking.db
      - App__FixedMeetLink=${FIXED_MEET_LINK}
      - R2__AccountId=${R2_ACCOUNT_ID}
      - R2__AccessKeyId=${R2_KEY_ID}
      - R2__SecretAccessKey=${R2_SECRET}
      - R2__Bucket=${R2_BUCKET}
      - Smtp__Host=${SMTP_HOST}
      - Smtp__Port=${SMTP_PORT}
      - Smtp__From=${SMTP_FROM}
      - App__Users__0__Email=${TEACHER_EMAIL}
      - App__Users__1__Email=${STUDENT_A_EMAIL}
      - App__Users__2__Email=${STUDENT_B_EMAIL}
    volumes:
      - booking-data:/app/data

  frontend:
    image: ghcr.io/${GH_OWNER}/class-booking-frontend:${TAG:-latest}
    restart: unless-stopped
    ports:
      - "127.0.0.1:8081:80"
    depends_on:
      - backend

volumes:
  booking-data:
```

(Backend is reached through frontend's nginx `/api` proxy; only frontend publishes a host port.)

- [ ] **Step 4: Build + smoke test locally**

```bash
docker build -t booking-backend ./backend
docker build -t booking-frontend ./frontend
docker run -d --name smoke-backend -e App__FixedMeetLink=https://meet.google.com/test booking-backend
docker run -d --name smoke-frontend --link smoke-backend:backend -p 8081:80 booking-frontend
curl -s http://localhost:8081/health   # proxied to backend
docker rm -f smoke-backend smoke-frontend
```

Expected: `{"status":"ok"}`.

- [ ] **Step 5: Commit**

```bash
git add backend/Dockerfile frontend/Dockerfile frontend/nginx.conf infra/ && git commit -m "build: docker images and compose"
```

---

### Task 11: GitHub Actions CI + deploy + branch protection + CodeRabbit

**Files:**
- Create: `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`, `.coderabbit.yaml`

- [ ] **Step 1: CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: ci
on:
  pull_request:
  push: { branches: [main] }

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet test
        working-directory: backend

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 22, cache: npm, cache-dependency-path: frontend/package-lock.json }
      - run: npm ci && npm run test:unit -- --run && npm run build
        working-directory: frontend

  e2e:
    runs-on: ubuntu-latest
    needs: [backend, frontend]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - uses: actions/setup-node@v4
        with: { node-version: 22 }
      - run: npm ci
        working-directory: frontend
      - run: npm ci && npx playwright install --with-deps && npm test
        working-directory: e2e

  build-images:
    if: github.ref == 'refs/heads/main' && github.event_name == 'push'
    runs-on: ubuntu-latest
    needs: [e2e]
    permissions: { contents: read, packages: write }
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/build-push-action@v6
        with:
          context: backend
          push: true
          tags: ghcr.io/${{ github.repository_owner }}/class-booking-backend:latest
      - uses: docker/build-push-action@v6
        with:
          context: frontend
          push: true
          tags: ghcr.io/${{ github.repository_owner }}/class-booking-frontend:latest
```

- [ ] **Step 2: Deploy workflow**

`.github/workflows/deploy.yml`:

```yaml
name: deploy
on:
  workflow_run:
    workflows: [ci]
    types: [completed]
    branches: [main]

jobs:
  deploy:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: pulumi/actions@v5
        with:
          command: up
          stack-name: prod
          work-dir: infra
        env:
          PULUMI_CONFIG_PASSPHRASE: ${{ secrets.PULUMI_PASSPHRASE }}
          DO_HOST: ${{ secrets.DO_HOST }}
          DO_USER: ${{ secrets.DO_USER }}
          DO_SSH_KEY: ${{ secrets.DO_SSH_KEY }}
          GH_OWNER: ${{ github.repository_owner }}
          FIXED_MEET_LINK: ${{ secrets.FIXED_MEET_LINK }}
          R2_ACCOUNT_ID: ${{ secrets.R2_ACCOUNT_ID }}
          R2_KEY_ID: ${{ secrets.R2_KEY_ID }}
          R2_SECRET: ${{ secrets.R2_SECRET }}
          R2_BUCKET: ${{ secrets.R2_BUCKET }}
          SMTP_HOST: ${{ secrets.SMTP_HOST }}
          SMTP_PORT: ${{ secrets.SMTP_PORT }}
          SMTP_FROM: ${{ secrets.SMTP_FROM }}
          TEACHER_EMAIL: ${{ secrets.TEACHER_EMAIL }}
          STUDENT_A_EMAIL: ${{ secrets.STUDENT_A_EMAIL }}
          STUDENT_B_EMAIL: ${{ secrets.STUDENT_B_EMAIL }}
```

- [ ] **Step 3: CodeRabbit config**

`.coderabbit.yaml`:

```yaml
language: en-US
early_access: false
reviews:
  profile: chill
  request_changes_workflow: true
  high_level_summary: true
  poem: false
  review_status: true
```

- [ ] **Step 4: Branch protection (one-time, local machine with admin GitHub token)**

```bash
gh api -X PUT repos/{owner}/{repo}/branches/main/protection \
  -f required_status_checks='{"strict":true,"contexts":["backend","frontend","e2e"]}' \
  -f enforce_admins=false \
  -f required_pull_request_reviews='{"required_approving_review_count":1, "require_code_owner_reviews":false}' \
  -f restrictions=null
```

Expected: `200 OK`. Then enable CodeRabbit on the repo (GitHub marketplace) and set required review = CodeRabbit review in repo settings if supported; otherwise the `request_changes_workflow: true` config makes its comments blocking via workflow.

- [ ] **Step 5: Commit**

```bash
git add .github/ .coderabbit.yaml && git commit -m "ci: test matrix, image build, deploy trigger, coderabbit"
```

---

### Task 12: Pulumi infra (existing droplet)

**Files:**
- Create: `infra/Pulumi.yaml`, `infra/Pulumi.prod.yaml`, `infra/Program.cs`, `infra/infra.csproj`, `infra/.gitignore` (state is local-only, git-ignored)

- [ ] **Step 1: Project files**

`infra/infra.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Pulumi" Version="3.*" />
  </ItemGroup>
</Project>
```

`infra/Pulumi.yaml`:

```yaml
name: class-booking
runtime: dotnet
description: Class booking deploy on existing droplet
```

`infra/Pulumi.prod.yaml`:

```yaml
config:
  class-booking:domain: lessons.example.com   # CHANGE before first `pulumi up`
```

`infra/Program.cs` — runs over SSH against the existing droplet (Command provider), no cloud resources:

```csharp
using Pulumi;
using Pulumi.Command.Local;
using System.Text;

return await Deployment.RunAsync(() =>
{
    var domain = Deployment.Instance.GetConfigValue("class-booking:domain")
                 ?? throw new Exception("set class-booking:domain in Pulumi.prod.yaml");

    var host = Environment.GetEnvironmentVariable("DO_HOST")
               ?? throw new Exception("DO_HOST env missing");
    var user = Environment.GetEnvironmentVariable("DO_USER") ?? "root";
    var keyB64 = Environment.GetEnvironmentVariable("DO_SSH_KEY")
                 ?? throw new Exception("DO_SSH_KEY env missing (base64 private key)");
    var ghOwner = (Environment.GetEnvironmentVariable("GH_OWNER") ?? "").ToLowerInvariant();

    var envKeys = new[]
    {
        "FIXED_MEET_LINK", "R2_ACCOUNT_ID", "R2_KEY_ID", "R2_SECRET", "R2_BUCKET",
        "SMTP_HOST", "SMTP_PORT", "SMTP_FROM", "TEACHER_EMAIL", "STUDENT_A_EMAIL", "STUDENT_B_EMAIL"
    };
    var envContent = new StringBuilder($"GH_OWNER={ghOwner}\n");
    foreach (var k in envKeys)
    {
        var v = Environment.GetEnvironmentVariable(k);
        if (!string.IsNullOrEmpty(v)) envContent.AppendLine($"{k}={v}");
    }

    string ssh(string cmd) =>
        $"ssh -i /tmp/do_key -o StrictHostKeyChecking=accept-new {user}@{host} '{cmd.Replace("'", "'\\''")}'";

    var writeKey = new Command("write-ssh-key", new CommandArgs
    {
        Create = $"echo {keyB64} | base64 -d > /tmp/do_key && chmod 600 /tmp/do_key",
    });

    var writeEnv = new Command("write-env", new CommandArgs
    {
        Create = $"cat > /tmp/cb.env <<'ENVEOF'\n{envContent}ENVEOF\n" +
                 ssh("mkdir -p /opt/class-booking") + " && " +
                 "scp -i /tmp/do_key /tmp/cb.env " + $"{user}@{host}:/opt/class-booking/.env",
    }, new CustomResourceOptions { DependsOn = { writeKey } });

    var deploy = new Command("deploy", new CommandArgs
    {
        Create =
            $"scp -i /tmp/do_key infra/docker-compose.yml {user}@{host}:/opt/class-booking/docker-compose.yml && " +
            ssh("cd /opt/class-booking && docker compose pull && docker compose up -d"),
    }, new CustomResourceOptions { DependsOn = { writeEnv } });

    var vhostConf =
        $"server {{\n" +
        $"    listen 80;\n" +
        $"    server_name {domain};\n" +
        $"    location / {{\n" +
        $"        proxy_pass http://127.0.0.1:8081;\n" +
        $"        proxy_set_header Host $host;\n" +
        $"        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;\n" +
        $"    }}\n" +
        $"}}\n";

    var vhost = new Command("nginx-vhost", new CommandArgs
    {
        Create = $"cat > /tmp/{domain}.conf <<'NGINXEOF'\n{vhostConf}NGINXEOF\n" +
                 $"scp -i /tmp/do_key /tmp/{domain}.conf {user}@{host}:/etc/nginx/sites-available/{domain} && " +
                 ssh($"ln -sf /etc/nginx/sites-available/{domain} /etc/nginx/sites-enabled/ && nginx -t && systemctl reload nginx"),
    }, new CustomResourceOptions { DependsOn = { deploy } });

    return new Dictionary<string, object?> { ["url"] = $"http://{domain}" };
});
```

Notes for the executor:
- `$host`/`$proxy_add_x_forwarded_for` are nginx variables inside a raw here-doc — they must reach the droplet literally, hence the single-quoted `NGINXEOF` heredoc.
- TLS: after first `pulumi up`, run once on the droplet: `certbot --nginx -d <domain>` (existing certbot handles renewal). If a wildcard cert already covers the subdomain, extend `vhostConf` with its `ssl_certificate` lines + `listen 443 ssl;` instead.
- Pulumi state stays local (`pulumi login file://$HOME/.pulumi-local`); CI gets fresh state each run — commands are idempotent (compose up, vhost overwrite), so absence of remote state is safe.

`infra/.gitignore`:

```
.pulumi/
*.state
.env
```

- [ ] **Step 2: Validate**

```bash
cd infra && dotnet build
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add infra/ && git commit -m "infra: pulumi deploy to existing droplet via ssh"
```

---

### Task 13: GitHub repo + secrets + first deploy + acceptance

**Files:**
- Modify: README.md (deploy docs)

- [ ] **Step 1: Create GitHub repo + push**

```bash
gh repo create class-booking-system --private --source . --push
```

- [ ] **Step 2: Set secrets**

```bash
gh secret set DO_HOST --body "<droplet ip>"
gh secret set DO_USER --body "root"
gh secret set DO_SSH_KEY --body "$(base64 -w0 ~/.ssh/id_ed25519)"
gh secret set PULUMI_PASSPHRASE --body "<random passphrase>"
gh secret set FIXED_MEET_LINK --body "<your recurring meet link>"
gh secret set R2_ACCOUNT_ID --body "<cloudflare account id>"
gh secret set R2_KEY_ID --body "<r2 access key id>"
gh secret set R2_SECRET --body "<r2 secret>"
gh secret set R2_BUCKET --body "class-booking-backups"
gh secret set SMTP_HOST --body "<smtp host>" && gh secret set SMTP_PORT --body "587" && gh secret set SMTP_FROM --body "<from email>"
gh secret set TEACHER_EMAIL --body "<your email>"
gh secret set STUDENT_A_EMAIL --body "<student a email>"
gh secret set STUDENT_B_EMAIL --body "<student b email>"
```

Also create the R2 bucket + access key in the Cloudflare dashboard (account → R2), and set real student emails.

- [ ] **Step 3: Update domain config + first deploy**

- Edit `infra/Pulumi.prod.yaml` → real subdomain.
- Add DNS A record `<subdomain>` → droplet IP (existing DNS provider).
- Open PR (any branch) → CI must go green → merge → deploy workflow runs → `pulumi up`.

Verify:

```bash
curl -s https://<subdomain>/health        # {"status":"ok"}
```

- [ ] **Step 4: TLS + manual acceptance checklist**

```bash
ssh root@<droplet> "certbot --nginx -d <subdomain> -n --redirect"
```

Manual checks (do these in a browser):
1. student A logs in with real email code (SMTP working)
2. books a day, sees it on calendar
3. student B books same day → combined
4. cancel + reschedule work
5. admin blocks/unblocks
6. `.ics` download opens in Calendar app at 20:30 Harare / local time
7. next night: admin page shows last R2 backup time

- [ ] **Step 5: Update README deploy section + final commit**

```bash
git add README.md infra/Pulumi.prod.yaml && git commit -m "docs: deploy runbook" && git push
```

(Ensure `Pulumi.prod.yaml` domain committed is the real one — it's not secret.)

---

## Self-Review (completed during planning)

1. **Spec coverage:** slice-1 items — booking/cancel/reschedule (T3/T6), shared calendar FOMO (T8), combined auto (T2/T6), admin blocking (T3/T6/T8), magic-code auth (T5), fixed Meet link + ics (T4/T6), R2 backup/restore (T4/T6/T8), TZ auto (T2/T7/T8), compliance pages (T7), E2E (T9 + spec journeys), CI/CD + protection + CodeRabbit (T11), Pulumi existing droplet (T12/T13), Docker (T10). Slice 2 (Google OAuth prod + Twilio trio) intentionally excluded — separate plan per phased approach.
2. **Placeholders:** `lessons.example.com`, `CHANGE-ME`, `<...>` are runtime configuration values filled in T13 — acceptable; no TBD/TODO/"pick one" steps remain.
3. **Type consistency:** `SlotDayDto` ↔ frontend `SlotDay` (casing via ASP.NET camelCase JSON), `IMeetLinkProvider` port in Application implemented by `FixedLinkMeetProvider`, `BookingException` mapped in endpoint layer, single `Sender` dispatch implementation, `ApiFactory.LastCode` written only by `FakeSender` after request-code.
