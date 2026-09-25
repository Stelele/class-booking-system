# Google OAuth CI Rollout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Google Calendar slot lifecycle reversible, deploy the existing GitHub Actions pipeline with secure OAuth runtime secrets, connect the teacher account, and prove event creation/deletion with one real booking.

**Architecture:** Add one focused application helper that distinguishes active shared slots from releasable unused slots. Creation and rescheduling refresh legacy unused links, while cancellation and rescheduling clear released Google fields and delete events exactly once. Keep the existing CI/CD workflows unchanged: configure GitHub secrets directly, deploy in fixed-link mode for consent, switch `MEET_PROVIDER` only after OAuth succeeds, and roll back to fixed mode on failure.

**Tech Stack:** .NET 10, EF Core InMemory/SQLite, xUnit, GitHub Actions, Pulumi, Google OAuth 2.0 and Calendar API, GitHub CLI, Chrome DevTools

---

## Execution Preconditions

- Subagents are unavailable; execute inline with `executing-plans`.
- Before editing, load `using-git-worktrees`, create an isolated worktree from the current `main`, and follow its safety checks.
- Obtain explicit user permission before creating implementation commits and before pushing `HEAD` to `origin/main`.
- Never print, copy into a command argument, or commit OAuth credential values, refresh tokens, or `GOOGLE_TOKEN_KEY`.
- The user performs teacher/student email-code login and Google consent in the browser.
- No lint script exists. Required checks are backend tests, frontend tests/typecheck/build, and Playwright E2E.

## File Map

| File | Responsibility |
|---|---|
| `backend/Application/Bookings/BookingSlotLifecycle.cs` | Determine whether a slot is unused or actively shared, clear released Meet fields, and return the Google event ID |
| `backend/Application/Bookings/CreateBookingCommandHandler.cs` | Refresh a legacy link when the destination slot has no active bookings |
| `backend/Application/Bookings/CancelBookingCommandHandler.cs` | Release the cancelled booking's slot after the last active booking leaves |
| `backend/Application/Bookings/RescheduleBookingCommandHandler.cs` | Release the old slot after the last active booking moves, including moves to an existing slot |
| `backend/Tests/Bookings/BookingLifecycleTests.cs` | Focused in-memory regression tests and recording fakes |
| `.github/workflows/ci.yml` | Existing automated checks; no changes |
| `.github/workflows/deploy.yml` | Existing deployment secret passthrough; no changes |
| GitHub repository secrets | Runtime OAuth configuration and provider selection; no source file |

### Task 1: Release Google slots on final cancellation

**Files:**
- Create: `backend/Application/Bookings/BookingSlotLifecycle.cs`
- Modify: `backend/Application/Bookings/CancelBookingCommandHandler.cs:7-27`
- Create: `backend/Tests/Bookings/BookingLifecycleTests.cs`

- [ ] **Step 1: Write the failing cancellation lifecycle tests**

Create `backend/Tests/Bookings/BookingLifecycleTests.cs` with:

```csharp
using Application.Abstractions;
using Application.Bookings;
using Domain.Slots;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tests.Bookings;

public sealed class BookingLifecycleTests
{
    [Fact]
    public async Task Cancel_last_booking_releases_slot_and_rebooking_creates_new_link()
    {
        await using var db = NewDb();
        var studentId = Guid.NewGuid();
        var date = FutureDate(30);
        var (slot, booking) = await SeedAsync(
            db, date, studentId, "https://meet.google.com/old", "old-event");
        var current = new StubCurrentUser(studentId);
        var events = new RecordingMeetEventSync();
        var provider = new RecordingMeetLinkProvider();

        var cancel = new CancelBookingCommandHandler(
            db, current, events, new NullBookingNotifier());
        await cancel.Handle(new CancelBookingCommand(booking.Id), CancellationToken.None);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Null(slot.MeetLink);
        Assert.Null(slot.GoogleEventId);
        Assert.Equal(new[] { "old-event" }, events.DeletedEventIds);

        var create = new CreateBookingCommandHandler(
            db, current, provider, new NullBookingNotifier());
        var rebooked = await create.Handle(
            new CreateBookingCommand(date), CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("https://meet.google.com/new-1", rebooked.MeetLink);
        Assert.Equal("https://meet.google.com/new-1", slot.MeetLink);
        Assert.Equal("new-event-1", slot.GoogleEventId);
    }

    [Fact]
    public async Task Cancel_shared_booking_keeps_slot_and_event()
    {
        await using var db = NewDb();
        var firstStudentId = Guid.NewGuid();
        var secondStudentId = Guid.NewGuid();
        var date = FutureDate(31);
        var (slot, firstBooking) = await SeedAsync(
            db, date, firstStudentId, "https://meet.google.com/shared", "shared-event");
        var secondBooking = new Booking
        {
            SlotId = slot.Id,
            StudentId = secondStudentId,
        };
        db.Add(secondBooking);
        await db.SaveChangesAsync();
        var events = new RecordingMeetEventSync();

        var cancel = new CancelBookingCommandHandler(
            db, new StubCurrentUser(firstStudentId), events, new NullBookingNotifier());
        await cancel.Handle(
            new CancelBookingCommand(firstBooking.Id), CancellationToken.None);

        Assert.Equal(BookingStatus.Cancelled, firstBooking.Status);
        Assert.Equal(BookingStatus.Active, secondBooking.Status);
        Assert.Equal("https://meet.google.com/shared", slot.MeetLink);
        Assert.Equal("shared-event", slot.GoogleEventId);
        Assert.Empty(events.DeletedEventIds);
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static DateOnly FutureDate(int daysAhead)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        while (date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
        return date;
    }

    private static async Task<(Slot Slot, Booking Booking)> SeedAsync(
        AppDbContext db,
        DateOnly date,
        Guid studentId,
        string meetLink,
        string? googleEventId)
    {
        var slot = new Slot
        {
            Date = date,
            MeetLink = meetLink,
            GoogleEventId = googleEventId,
        };
        var booking = new Booking
        {
            Slot = slot,
            StudentId = studentId,
        };
        slot.Bookings.Add(booking);
        db.Add(slot);
        await db.SaveChangesAsync();
        return (slot, booking);
    }

    private sealed record StubCurrentUser(
        Guid UserId,
        string Name = "Student",
        bool IsAdmin = false) : ICurrentUser;

    private sealed class RecordingMeetLinkProvider : IMeetLinkProvider
    {
        public int Calls { get; private set; }

        public Task<MeetLinkResult> GetOrCreateLinkAsync(
            DateOnly date, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new MeetLinkResult(
                $"https://meet.google.com/new-{Calls}",
                $"new-event-{Calls}"));
        }
    }

    private sealed class RecordingMeetEventSync : IMeetEventSync
    {
        public List<string> DeletedEventIds { get; } = [];

        public Task DeleteEventAsync(
            string googleEventId, CancellationToken ct = default)
        {
            DeletedEventIds.Add(googleEventId);
            return Task.CompletedTask;
        }
    }

    private sealed class NullBookingNotifier : IBookingNotifier
    {
        public Task NotifyBookingChangedAsync(
            Guid bookingId, BookingChangeKind kind, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run from the repository root:

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests.Cancel_"
```

Expected: `Cancel_last_booking_releases_slot_and_rebooking_creates_new_link` fails because `slot.MeetLink` and `slot.GoogleEventId` remain populated after cancellation. `Cancel_shared_booking_keeps_slot_and_event` passes.

- [ ] **Step 3: Add the slot-release helper**

Create `backend/Application/Bookings/BookingSlotLifecycle.cs` with:

```csharp
using Application.Abstractions;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

internal static class BookingSlotLifecycle
{
    public static async Task<string?> ReleaseIfUnusedAsync(
        IAppDbContext db,
        Slot slot,
        Guid bookingId,
        CancellationToken ct)
    {
        var hasOtherActiveBooking = await db.Bookings.AnyAsync(
            b => b.SlotId == slot.Id
                && b.Id != bookingId
                && b.Status == BookingStatus.Active,
            ct);
        if (hasOtherActiveBooking) return null;

        var googleEventId = slot.GoogleEventId;
        slot.MeetLink = null;
        slot.GoogleEventId = null;
        return googleEventId;
    }
}
```

- [ ] **Step 4: Make cancellation release an unused slot**

Replace `backend/Application/Bookings/CancelBookingCommandHandler.cs` with:

```csharp
using Application.Abstractions;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

public sealed class CancelBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetEventSync sync, IBookingNotifier notifier)
    : ICommandHandler<CancelBookingCommand, bool>
{
    public async Task<bool> Handle(CancelBookingCommand c, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Slot)
            .FirstOrDefaultAsync(b => b.Id == c.BookingId, ct)
            ?? throw new BookingException("Booking not found.");

        if (booking.StudentId != user.UserId && !user.IsAdmin)
            throw new BookingException("You can only cancel your own bookings.");

        var googleEventId = await BookingSlotLifecycle.ReleaseIfUnusedAsync(
            db, booking.Slot, booking.Id, ct);
        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (googleEventId is not null)
            await sync.DeleteEventAsync(googleEventId, ct);
        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Cancelled, ct);
        return true;
    }
}
```

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run:

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests.Cancel_"
```

Expected: 2 tests pass, 0 fail.

- [ ] **Step 6: Run all backend tests**

Run:

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass, 0 fail.

- [ ] **Step 7: Commit the cancellation fix after explicit authorization**

Run:

```bash
git add backend/Application/Bookings/BookingSlotLifecycle.cs backend/Application/Bookings/CancelBookingCommandHandler.cs backend/Tests/Bookings/BookingLifecycleTests.cs
git commit -m "fix(bookings): release Google slot on final cancellation"
```

Expected: one commit containing only the three listed files.

### Task 2: Refresh legacy unused slots through the active provider

**Files:**
- Modify: `backend/Application/Bookings/BookingSlotLifecycle.cs:6-23`
- Modify: `backend/Application/Bookings/CreateBookingCommandHandler.cs:23-35`
- Modify: `backend/Application/Bookings/RescheduleBookingCommandHandler.cs:29-48`
- Modify: `backend/Tests/Bookings/BookingLifecycleTests.cs`

- [ ] **Step 1: Add failing legacy-slot tests**

Insert these methods immediately before `private static AppDbContext NewDb()`:

```csharp
    [Fact]
    public async Task Create_booking_refreshes_legacy_unused_slot()
    {
        await using var db = NewDb();
        var firstStudentId = Guid.NewGuid();
        var secondStudentId = Guid.NewGuid();
        var date = FutureDate(32);
        var (slot, cancelledBooking) = await SeedAsync(
            db, date, firstStudentId, "https://meet.google.com/legacy", null);
        cancelledBooking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();
        var provider = new RecordingMeetLinkProvider();

        var create = new CreateBookingCommandHandler(
            db, new StubCurrentUser(secondStudentId), provider, new NullBookingNotifier());
        var created = await create.Handle(
            new CreateBookingCommand(date), CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("https://meet.google.com/new-1", created.MeetLink);
        Assert.Equal("https://meet.google.com/new-1", slot.MeetLink);
        Assert.Equal("new-event-1", slot.GoogleEventId);
    }

    [Fact]
    public async Task Create_booking_reuses_link_for_active_shared_slot()
    {
        await using var db = NewDb();
        var firstStudentId = Guid.NewGuid();
        var secondStudentId = Guid.NewGuid();
        var date = FutureDate(33);
        var (slot, _) = await SeedAsync(
            db, date, firstStudentId, "https://meet.google.com/shared", "shared-event");
        var provider = new RecordingMeetLinkProvider();

        var create = new CreateBookingCommandHandler(
            db, new StubCurrentUser(secondStudentId), provider, new NullBookingNotifier());
        var created = await create.Handle(
            new CreateBookingCommand(date), CancellationToken.None);

        Assert.Equal(0, provider.Calls);
        Assert.Equal("https://meet.google.com/shared", created.MeetLink);
        Assert.Equal("shared-event", slot.GoogleEventId);
    }

    [Fact]
    public async Task Reschedule_refreshes_legacy_unused_destination()
    {
        await using var db = NewDb();
        var movingStudentId = Guid.NewGuid();
        var oldDate = FutureDate(34);
        var newDate = FutureDate(35);
        var (_, movingBooking) = await SeedAsync(
            db, oldDate, movingStudentId, "https://meet.google.com/old", "old-event");
        var targetSlot = new Slot
        {
            Date = newDate,
            MeetLink = "https://meet.google.com/legacy-target",
        };
        db.Add(targetSlot);
        await db.SaveChangesAsync();
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(movingStudentId), provider, events, new NullBookingNotifier());
        var moved = await reschedule.Handle(
            new RescheduleBookingCommand(movingBooking.Id, newDate), CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("https://meet.google.com/new-1", moved.MeetLink);
        Assert.Equal("https://meet.google.com/new-1", targetSlot.MeetLink);
        Assert.Equal("new-event-1", targetSlot.GoogleEventId);
    }

    [Fact]
    public async Task Reschedule_reuses_active_destination_link()
    {
        await using var db = NewDb();
        var movingStudentId = Guid.NewGuid();
        var targetStudentId = Guid.NewGuid();
        var oldDate = FutureDate(36);
        var targetDate = FutureDate(37);
        var (_, movingBooking) = await SeedAsync(
            db, oldDate, movingStudentId, "https://meet.google.com/old", "old-event");
        var (targetSlot, _) = await SeedAsync(
            db, targetDate, targetStudentId, "https://meet.google.com/target", "target-event");
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(movingStudentId), provider, events, new NullBookingNotifier());
        var moved = await reschedule.Handle(
            new RescheduleBookingCommand(movingBooking.Id, targetDate), CancellationToken.None);

        Assert.Equal(0, provider.Calls);
        Assert.Equal("https://meet.google.com/target", moved.MeetLink);
        Assert.Equal("target-event", targetSlot.GoogleEventId);
    }
```

- [ ] **Step 2: Run the focused tests and verify RED**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests.Create_|FullyQualifiedName~BookingLifecycleTests.Reschedule_refreshes_legacy_unused_destination|FullyQualifiedName~BookingLifecycleTests.Reschedule_reuses_active_destination"
```

Expected: the two `refreshes` tests fail because the legacy link is reused. The two active-slot tests pass.

- [ ] **Step 3: Add active-slot detection to the lifecycle helper**

Replace `backend/Application/Bookings/BookingSlotLifecycle.cs` with:

```csharp
using Application.Abstractions;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

internal static class BookingSlotLifecycle
{
    public static Task<bool> HasActiveBookingsAsync(
        IAppDbContext db,
        Slot slot,
        CancellationToken ct) =>
        db.Bookings.AnyAsync(
            b => b.SlotId == slot.Id && b.Status == BookingStatus.Active,
            ct);

    public static async Task<string?> ReleaseIfUnusedAsync(
        IAppDbContext db,
        Slot slot,
        Guid bookingId,
        CancellationToken ct)
    {
        var hasOtherActiveBooking = await db.Bookings.AnyAsync(
            b => b.SlotId == slot.Id
                && b.Id != bookingId
                && b.Status == BookingStatus.Active,
            ct);
        if (hasOtherActiveBooking) return null;

        var googleEventId = slot.GoogleEventId;
        slot.MeetLink = null;
        slot.GoogleEventId = null;
        return googleEventId;
    }
}
```

- [ ] **Step 4: Refresh unused slots when creating a booking**

Replace `backend/Application/Bookings/CreateBookingCommandHandler.cs` with:

```csharp
using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Domain.Slots.Booking;

namespace Application.Bookings;

public sealed class CreateBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet, IBookingNotifier notifier)
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

        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct);
        if (slot is null)
        {
            slot = new Slot { Date = c.Date };
            db.Slots.Add(slot);
        }

        if (slot.MeetLink is null
            || !await BookingSlotLifecycle.HasActiveBookingsAsync(db, slot, ct))
        {
            var link = await meet.GetOrCreateLinkAsync(c.Date, ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
        }

        var booking = new BookingEntity { SlotId = slot.Id, StudentId = user.UserId };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.Date, ct);
            if (winner is null) throw;
            var dupeActive = await db.Bookings.AnyAsync(b =>
                b.StudentId == user.UserId && b.Status == BookingStatus.Active &&
                b.SlotId == winner.Id, ct);
            if (dupeActive) throw new BookingException("You already have a booking on that day.");
            booking = new BookingEntity { SlotId = winner.Id, StudentId = user.UserId };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            slot = winner;
        }

        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Created, ct);

        return new BookingDto(booking.Id, c.Date, LessonTime.StartUtc(c.Date), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: null, MeetLink: slot.MeetLink);
    }
}
```

- [ ] **Step 5: Refresh unused destination slots when rescheduling**

In `backend/Application/Bookings/RescheduleBookingCommandHandler.cs`, replace the existing `if (slot.MeetLink is null)` block with:

```csharp
        if (slot.MeetLink is null
            || !await BookingSlotLifecycle.HasActiveBookingsAsync(db, slot, ct))
        {
            var link = await meet.GetOrCreateLinkAsync(c.NewDate, ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
        }
```

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run the Step 2 command again.

Expected: 4 tests pass, 0 fail.

- [ ] **Step 7: Run all lifecycle tests**

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests"
```

Expected: 6 tests pass, 0 fail.

- [ ] **Step 8: Run all backend tests**

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass, 0 fail.

- [ ] **Step 9: Commit the legacy-slot fix after explicit authorization**

```bash
git add backend/Application/Bookings/BookingSlotLifecycle.cs backend/Application/Bookings/CreateBookingCommandHandler.cs backend/Application/Bookings/RescheduleBookingCommandHandler.cs backend/Tests/Bookings/BookingLifecycleTests.cs
git commit -m "fix(bookings): refresh unused legacy slots"
```

Expected: one commit containing only the four listed files.

### Task 3: Release Google slots when the last booking moves

**Files:**
- Modify: `backend/Application/Bookings/RescheduleBookingCommandHandler.cs:8-87`
- Modify: `backend/Tests/Bookings/BookingLifecycleTests.cs`

- [ ] **Step 1: Add failing reschedule lifecycle tests**

Insert these methods immediately before `private static AppDbContext NewDb()` in `backend/Tests/Bookings/BookingLifecycleTests.cs`:

```csharp
    [Fact]
    public async Task Reschedule_last_booking_releases_old_slot()
    {
        await using var db = NewDb();
        var studentId = Guid.NewGuid();
        var oldDate = FutureDate(32);
        var newDate = FutureDate(33);
        var (oldSlot, booking) = await SeedAsync(
            db, oldDate, studentId, "https://meet.google.com/old", "old-event");
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(studentId), provider, events, new NullBookingNotifier());
        var moved = await reschedule.Handle(
            new RescheduleBookingCommand(booking.Id, newDate), CancellationToken.None);

        Assert.Equal(newDate, moved.Date);
        Assert.Equal("https://meet.google.com/new-1", moved.MeetLink);
        Assert.Null(oldSlot.MeetLink);
        Assert.Null(oldSlot.GoogleEventId);
        Assert.Equal(new[] { "old-event" }, events.DeletedEventIds);
    }

    [Fact]
    public async Task Reschedule_shared_booking_keeps_old_slot_and_event()
    {
        await using var db = NewDb();
        var movingStudentId = Guid.NewGuid();
        var stayingStudentId = Guid.NewGuid();
        var oldDate = FutureDate(34);
        var newDate = FutureDate(35);
        var (oldSlot, movingBooking) = await SeedAsync(
            db, oldDate, movingStudentId, "https://meet.google.com/shared", "shared-event");
        var stayingBooking = new Booking
        {
            SlotId = oldSlot.Id,
            StudentId = stayingStudentId,
        };
        db.Add(stayingBooking);
        await db.SaveChangesAsync();
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(movingStudentId), provider, events, new NullBookingNotifier());
        await reschedule.Handle(
            new RescheduleBookingCommand(movingBooking.Id, newDate), CancellationToken.None);

        Assert.Equal(BookingStatus.Active, stayingBooking.Status);
        Assert.Equal("https://meet.google.com/shared", oldSlot.MeetLink);
        Assert.Equal("shared-event", oldSlot.GoogleEventId);
        Assert.Empty(events.DeletedEventIds);
    }

    [Fact]
    public async Task Reschedule_to_existing_slot_releases_old_slot_and_deletes_old_event()
    {
        await using var db = NewDb();
        var movingStudentId = Guid.NewGuid();
        var targetStudentId = Guid.NewGuid();
        var oldDate = FutureDate(36);
        var targetDate = FutureDate(37);
        var (oldSlot, movingBooking) = await SeedAsync(
            db, oldDate, movingStudentId, "https://meet.google.com/old", "old-event");
        var (targetSlot, _) = await SeedAsync(
            db, targetDate, targetStudentId, "https://meet.google.com/target", "target-event");
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(movingStudentId), provider, events, new NullBookingNotifier());
        var moved = await reschedule.Handle(
            new RescheduleBookingCommand(movingBooking.Id, targetDate), CancellationToken.None);

        Assert.Equal("https://meet.google.com/target", moved.MeetLink);
        Assert.Equal(0, provider.Calls);
        Assert.Null(oldSlot.MeetLink);
        Assert.Null(oldSlot.GoogleEventId);
        Assert.Equal(new[] { "old-event" }, events.DeletedEventIds);
        Assert.Equal("https://meet.google.com/target", targetSlot.MeetLink);
        Assert.Equal("target-event", targetSlot.GoogleEventId);
    }

    [Fact]
    public async Task Reschedule_same_date_keeps_slot_and_event()
    {
        await using var db = NewDb();
        var studentId = Guid.NewGuid();
        var date = FutureDate(38);
        var (slot, booking) = await SeedAsync(
            db, date, studentId, "https://meet.google.com/same", "same-event");
        var provider = new RecordingMeetLinkProvider();
        var events = new RecordingMeetEventSync();

        var reschedule = new RescheduleBookingCommandHandler(
            db, new StubCurrentUser(studentId), provider, events, new NullBookingNotifier());
        await reschedule.Handle(
            new RescheduleBookingCommand(booking.Id, date), CancellationToken.None);

        Assert.Equal(0, provider.Calls);
        Assert.Equal("https://meet.google.com/same", slot.MeetLink);
        Assert.Equal("same-event", slot.GoogleEventId);
        Assert.Empty(events.DeletedEventIds);
    }
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests.Reschedule_"
```

Expected: 3 tests fail—the last-booking, shared-slot, and existing-target tests—because the old slot is not released and/or its event is deleted incorrectly. The 2 legacy-destination tests and the same-date test pass.

- [ ] **Step 3: Make reschedule release the old slot after a successful move**

Replace `backend/Application/Bookings/RescheduleBookingCommandHandler.cs` with:

```csharp
using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

public sealed class RescheduleBookingCommandHandler(IAppDbContext db, ICurrentUser user, IMeetLinkProvider meet, IMeetEventSync sync, IBookingNotifier notifier)
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

        var oldSlotId = booking.SlotId;
        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct);
        if (slot is null)
        {
            slot = new Slot { Date = c.NewDate };
            db.Slots.Add(slot);
        }

        if (slot.MeetLink is null
            || !await BookingSlotLifecycle.HasActiveBookingsAsync(db, slot, ct))
        {
            var link = await meet.GetOrCreateLinkAsync(c.NewDate, ct);
            slot.MeetLink = link.MeetLink;
            slot.GoogleEventId = link.GoogleEventId;
        }

        string? releasedGoogleEventId = null;
        if (oldSlotId != slot.Id)
        {
            releasedGoogleEventId = await BookingSlotLifecycle.ReleaseIfUnusedAsync(
                db, booking.Slot, booking.Id, ct);
        }

        var originalDate = booking.OriginalDate ?? booking.Slot.Date;
        booking.OriginalDate = originalDate;
        booking.SlotId = slot.Id;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Slots.FirstOrDefaultAsync(s => s.Date == c.NewDate, ct);
            if (winner is null) throw;
            var dupeActive = await db.Bookings.AnyAsync(b =>
                b.StudentId == booking.StudentId && b.Status == BookingStatus.Active &&
                b.SlotId == winner.Id && b.Id != booking.Id, ct);
            if (dupeActive) throw new BookingException("You already have a booking on the new day.");
            booking = await db.Bookings.FirstAsync(b => b.Id == booking.Id, ct);
            booking.OriginalDate = originalDate;
            booking.SlotId = winner.Id;
            booking.UpdatedAtUtc = DateTime.UtcNow;
            slot = winner;
            if (oldSlotId != slot.Id)
            {
                var oldSlot = await db.Slots.FirstAsync(s => s.Id == oldSlotId, ct);
                releasedGoogleEventId = await BookingSlotLifecycle.ReleaseIfUnusedAsync(
                    db, oldSlot, booking.Id, ct);
            }
            await db.SaveChangesAsync(ct);
        }

        if (releasedGoogleEventId is not null)
            await sync.DeleteEventAsync(releasedGoogleEventId, ct);
        await notifier.NotifyBookingChangedAsync(booking.Id, BookingChangeKind.Rescheduled, ct);

        return new BookingDto(booking.Id, c.NewDate, LessonTime.StartUtc(c.NewDate), user.Name,
            CanCancel: true, CanReschedule: true, OriginalDate: booking.OriginalDate, MeetLink: slot.MeetLink);
    }
}
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run:

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests.Reschedule_"
```

Expected: 6 reschedule tests pass, 0 fail.

- [ ] **Step 5: Run all lifecycle tests**

Run:

```bash
dotnet test backend/Tests/Tests.csproj --filter "FullyQualifiedName~BookingLifecycleTests"
```

Expected: 10 lifecycle tests pass, 0 fail.

- [ ] **Step 6: Run all backend tests**

Run:

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass, 0 fail.

- [ ] **Step 7: Commit the reschedule fix after explicit authorization**

Run:

```bash
git add backend/Application/Bookings/RescheduleBookingCommandHandler.cs backend/Tests/Bookings/BookingLifecycleTests.cs
git commit -m "fix(bookings): release Google slot after reschedule"
```

Expected: one commit containing only the two listed files.

### Task 4: Run the complete repository verification suite

**Files:**
- Verify only; no source changes expected

- [ ] **Step 1: Run backend tests**

```bash
dotnet test backend/BookingApi.slnx
```

Expected: all tests pass, 0 fail.

- [ ] **Step 2: Run frontend unit tests**

```bash
npm --prefix frontend test
```

Expected: Vitest exits 0 with all tests passing.

- [ ] **Step 3: Run frontend type checking**

```bash
npm --prefix frontend run typecheck
```

Expected: `vue-tsc --noEmit` exits 0.

- [ ] **Step 4: Build the frontend**

```bash
npm --prefix frontend run build
```

Expected: Vite production build exits 0.

- [ ] **Step 5: Run Playwright E2E tests**

```bash
npm --prefix e2e test
```

Expected: Playwright exits 0 with all journeys passing.

- [ ] **Step 6: Check patch integrity and repository state**

```bash
git diff --check
git status --short
```

Expected: `git diff --check` prints nothing. `git status --short` shows no uncommitted implementation files; ignored build outputs do not appear.

### Task 5: Publish the lifecycle fix and verify existing CI/CD

**Files:**
- No source changes

- [ ] **Step 1: Obtain explicit permission to push implementation commits to `main`**

Ask the user for permission to run:

```bash
git push origin HEAD:main
```

Do not push without an explicit yes.

- [ ] **Step 2: Push the verified commits**

After approval:

```bash
git push origin HEAD:main
```

Expected: push succeeds without force.

- [ ] **Step 3: Wait for the CI run created by this commit**

```bash
sha=$(git rev-parse HEAD)
id=""
for attempt in $(seq 1 30); do
  id=$(gh run list --workflow ci --commit "$sha" --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$id" ]; then break; fi
  sleep 2
done
test -n "$id"
gh run watch "$id" --exit-status
```

Expected: the `ci` workflow succeeds. Backend, frontend, E2E, and image-build jobs are successful.

- [ ] **Step 4: Wait for the automatic deploy workflow**

```bash
id=""
for attempt in $(seq 1 45); do
  id=$(gh run list --workflow deploy --commit "$sha" --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$id" ]; then break; fi
  sleep 2
done
test -n "$id"
gh run watch "$id" --exit-status
```

Expected: the `deploy` workflow succeeds.

- [ ] **Step 5: Verify production health in fixed-link mode**

```bash
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected:

```json
{"status":"ok"}
```

Do not configure Google secrets until this deployment is green.

### Task 6: Configure Google runtime secrets and redeploy in fixed mode

**Files:**
- Local credential source: `/home/gift/Downloads/client_secret_883996886062-vlfsineq3vtn4jelovfkv1bkdionenq8.apps.googleusercontent.com.json`
- GitHub repository secrets only; no file changes

- [ ] **Step 1: Validate the OAuth JSON without printing values**

```bash
python3 - <<'PY'
import json
from pathlib import Path

path = Path("/home/gift/Downloads/client_secret_883996886062-vlfsineq3vtn4jelovfkv1bkdionenq8.apps.googleusercontent.com.json")
data = json.loads(path.read_text())
web = data.get("web")
expected = "https://lessons.giftmugweni.com/api/auth/google/callback"
assert isinstance(web, dict), "missing web OAuth client"
assert web.get("client_id"), "missing client_id"
assert web.get("client_secret"), "missing client_secret"
assert web.get("redirect_uris") == [expected], "unexpected redirect URI"
print("OAuth web client JSON is valid")
PY
```

Expected: only `OAuth web client JSON is valid` is printed.

- [ ] **Step 2: Pipe credential fields directly into GitHub secrets**

```bash
set -euo pipefail
python3 -c 'import json,sys; d=json.load(open("/home/gift/Downloads/client_secret_883996886062-vlfsineq3vtn4jelovfkv1bkdionenq8.apps.googleusercontent.com.json"))["web"]; sys.stdout.write(d["client_id"])' | gh secret set GOOGLE_CLIENT_ID
python3 -c 'import json,sys; d=json.load(open("/home/gift/Downloads/client_secret_883996886062-vlfsineq3vtn4jelovfkv1bkdionenq8.apps.googleusercontent.com.json"))["web"]; sys.stdout.write(d["client_secret"])' | gh secret set GOOGLE_CLIENT_SECRET
python3 -c 'import json,sys; d=json.load(open("/home/gift/Downloads/client_secret_883996886062-vlfsineq3vtn4jelovfkv1bkdionenq8.apps.googleusercontent.com.json"))["web"]; sys.stdout.write(d["redirect_uris"][0])' | gh secret set GOOGLE_REDIRECT_URI
openssl rand -base64 32 | tr -d '\n' | gh secret set GOOGLE_TOKEN_KEY
```

Expected: all four commands exit 0 and print no credential values.

- [ ] **Step 3: Verify secret names and fixed provider state**

```bash
gh secret list
```

Expected names include:

```text
GOOGLE_CLIENT_ID
GOOGLE_CLIENT_SECRET
GOOGLE_REDIRECT_URI
GOOGLE_TOKEN_KEY
```

Expected: `MEET_PROVIDER` is absent, so compose defaults to `fixed`.

- [ ] **Step 4: Trigger a manual deployment**

Capture the current latest deploy ID, trigger the workflow, and watch only the new run:

```bash
before=$(gh run list --workflow deploy --limit 1 --json databaseId --jq '.[0].databaseId')
gh workflow run deploy.yml --ref main
id=""
for attempt in $(seq 1 30); do
  id=$(gh run list --workflow deploy --event workflow_dispatch --branch main --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$id" ] && [ "$id" != "$before" ]; then break; fi
  sleep 2
done
test -n "$id" && test "$id" != "$before"
gh run watch "$id" --exit-status
```

Expected: the manually triggered deploy succeeds.

- [ ] **Step 5: Verify production remains healthy**

```bash
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected:

```json
{"status":"ok"}
```

The application must still use the fixed link because `MEET_PROVIDER` is absent.

### Task 7: Connect Google interactively while fixed mode is active

**Files:**
- No source changes

- [ ] **Step 1: Open the production admin page**

Use Chrome DevTools to navigate to:

```text
https://lessons.giftmugweni.com/admin
```

Expected: the admin login flow appears if no authenticated teacher session exists.

- [ ] **Step 2: Let the user authenticate as the teacher**

The user enters the configured teacher email, receives the email code, and enters the code. Do not request or handle the user's password or email-code contents.

Expected: the browser reaches `/admin`.

- [ ] **Step 3: Start the OAuth flow**

Click **Connect Google**.

Expected: the browser redirects to Google's consent screen. Pause while the user selects the teacher Google account and grants Calendar event permission.

- [ ] **Step 4: Verify the callback succeeded**

Expected URL after redirect:

```text
https://lessons.giftmugweni.com/admin?google=connected
```

After the frontend removes the query parameter, evaluate this in the authenticated page:

```javascript
async () => {
  const response = await fetch('/api/admin/google/status');
  return { status: response.status, body: await response.json() };
}
```

Expected:

```json
{
  "status": 200,
  "body": {
    "connected": true,
    "needsReconnect": false
  }
}
```

Expected UI: the success alert appears briefly, then **Connect Google** and **Reconnect Google** are absent.

- [ ] **Step 5: Stop on OAuth failure**

If the URL contains `google=error`, the status is disconnected, or the page shows a reconnect requirement, stop. Keep `MEET_PROVIDER` unset, inspect sanitized deploy/application logs, and do not run Task 8.

### Task 8: Switch the provider to Google and deploy

**Files:**
- GitHub repository secret only; no source changes

- [ ] **Step 1: Set the provider secret**

```bash
gh secret set MEET_PROVIDER --body google
```

Expected: command exits 0. No other Google secret is changed.

- [ ] **Step 2: Trigger and watch a manual deploy**

```bash
before=$(gh run list --workflow deploy --limit 1 --json databaseId --jq '.[0].databaseId')
gh workflow run deploy.yml --ref main
id=""
for attempt in $(seq 1 30); do
  id=$(gh run list --workflow deploy --event workflow_dispatch --branch main --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$id" ] && [ "$id" != "$before" ]; then break; fi
  sleep 2
done
test -n "$id" && test "$id" != "$before"
gh run watch "$id" --exit-status
```

Expected: deploy succeeds. Startup validates `GOOGLE_TOKEN_KEY` as exactly 32 decoded bytes.

- [ ] **Step 3: Verify production health**

```bash
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected:

```json
{"status":"ok"}
```

- [ ] **Step 4: Roll back immediately if Google-mode startup or health fails**

```bash
gh secret set MEET_PROVIDER --body fixed
before=$(gh run list --workflow deploy --limit 1 --json databaseId --jq '.[0].databaseId')
gh workflow run deploy.yml --ref main
id=""
for attempt in $(seq 1 30); do
  id=$(gh run list --workflow deploy --event workflow_dispatch --branch main --limit 1 --json databaseId --jq '.[0].databaseId // empty')
  if [ -n "$id" ] && [ "$id" != "$before" ]; then break; fi
  sleep 2
done
test -n "$id" && test "$id" != "$before"
gh run watch "$id" --exit-status
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
```

Expected: fixed mode is restored and healthy. Do not remove or rotate the OAuth/token secrets.

### Task 9: Run one reversible production booking smoke test

**Files:**
- No source changes

- [ ] **Step 1: Open an isolated student browser context**

Create a new browser page with isolated context name `student-google-smoke` and navigate to:

```text
https://lessons.giftmugweni.com/login
```

This preserves the authenticated teacher/admin context in the original browser context.

- [ ] **Step 2: Let the user authenticate as one student**

The user enters one configured student email and completes the emailed login code.

Expected: the browser reaches `/calendar`.

- [ ] **Step 3: Select a far-future unused non-Sunday date**

Evaluate this in the authenticated student page:

```javascript
async () => {
  const start = new Date();
  start.setUTCDate(start.getUTCDate() + 35);
  for (let offset = 0; offset < 12; offset += 1) {
    const candidate = new Date(start);
    candidate.setUTCDate(candidate.getUTCDate() + offset * 7);
    if (candidate.getUTCDay() === 0) continue;
    const year = candidate.getUTCFullYear();
    const month = candidate.getUTCMonth() + 1;
    const daysResponse = await fetch(`/api/slots?year=${year}&month=${month}`);
    if (!daysResponse.ok) throw new Error(`Month preflight failed: ${daysResponse.status}`);
    const days = await daysResponse.json();
    const day = days.find(item => item.date.startsWith(`${year}-${String(month).padStart(2, "0")}-`));
    if (day?.state !== "Bookable" || !day.canBook) continue;
    const date = day.date;
    const icsResponse = await fetch(`/api/slots/${date}/ics`);
    if (icsResponse.status === 404) return date;
    if (!icsResponse.ok) throw new Error(`ICS preflight failed: ${icsResponse.status}`);
  }
  throw new Error('No unused far-future date found');
}
```

Expected: one `YYYY-MM-DD` date is returned. A 404 proves the date has no existing slot link.

- [ ] **Step 4: Create the booking through the UI**

Navigate the calendar to the returned month, click the returned date, and click **Confirm booking**.

Expected: the selected cell shows the student's name.

- [ ] **Step 5: Verify a Google link was created**

Evaluate for the selected date:

```javascript
async date => {
  const response = await fetch(`/api/slots/${date}/ics`);
  const body = await response.text();
  const location = body.match(/^LOCATION:(.+)$/m)?.[1]?.trim() ?? null;
  return { status: response.status, location };
}
```

Expected: status is 200 and `location` is a `https://meet.google.com/...` URL. Ask the user to confirm that a new event for that date appears on the teacher Google Calendar with a Meet link and the student invited.

- [ ] **Step 6: Cancel through the UI**

Open **My Lessons**, click **Cancel** for the test booking, and confirm the modal.

Expected: the booking disappears from upcoming lessons. The app runs its configured notification path; Twilio is currently unconfigured, so this may be log-only and notification delivery is not a Google smoke-test criterion.

- [ ] **Step 7: Verify Calendar deletion and slot release**

Ask the user to confirm the test event disappears from Google Calendar. Treat notification delivery as optional because Twilio is currently unconfigured.

Evaluate the same ICS URL again:

```javascript
async date => {
  const response = await fetch(`/api/slots/${date}/ics`, { method: "GET" });
  return { status: response.status };
}
```

Expected: status is 404 because cancellation cleared `MeetLink` and `GoogleEventId` after the last active booking was removed.

- [ ] **Step 8: Run final health and CI checks**

```bash
curl --fail --silent --show-error https://lessons.giftmugweni.com/health
gh run list --workflow ci --branch main --limit 3
gh run list --workflow deploy --branch main --limit 3
```

Expected: health is OK; the latest CI and Google-mode deploy runs are successful.

- [ ] **Step 9: Verify repository and secret hygiene**

```bash
git status --short
git log --oneline -5
gh secret list
```

Expected: no uncommitted implementation files; the lifecycle-fix commits are present; GitHub lists secret names/timestamps only; no OAuth JSON or secret value was added to git.

- [ ] **Step 10: Roll back if the smoke test fails**

If Google event creation fails, set `MEET_PROVIDER=fixed`, deploy using Task 8 Step 4, verify health, and preserve the OAuth/token secrets for diagnosis. If event creation succeeds but deletion fails, cancel any remaining active booking, keep the provider fixed until the stored event ID is reconciled, and report the exact failing stage.
