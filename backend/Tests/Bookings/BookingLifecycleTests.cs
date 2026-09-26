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
        var newDate = FutureDate(35, oldDate);
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
        var targetDate = FutureDate(37, oldDate);
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

    [Fact]
    public async Task Reschedule_last_booking_releases_old_slot()
    {
        await using var db = NewDb();
        var studentId = Guid.NewGuid();
        var oldDate = FutureDate(32);
        var newDate = FutureDate(33, oldDate);
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
        var newDate = FutureDate(35, oldDate);
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
        var targetDate = FutureDate(37, oldDate);
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

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // Sundays have no lessons, so they are skipped. Pass the previous date as
    // `after` whenever a test needs two distinct days: skipping a Sunday by
    // adding one can otherwise land on the very date the next offset returns.
    private static DateOnly FutureDate(int daysAhead, DateOnly? after = null)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead);
        while (date.DayOfWeek == DayOfWeek.Sunday || (after is not null && date <= after))
        {
            date = date.AddDays(1);
        }
        return date;
    }

    [Fact]
    public void FutureDate_gives_distinct_non_sundays_for_consecutive_offsets()
    {
        for (var ahead = 1; ahead <= 60; ahead++)
        {
            var date = FutureDate(ahead);
            var next = FutureDate(ahead + 1, date);
            Assert.NotEqual(DayOfWeek.Sunday, date.DayOfWeek);
            Assert.NotEqual(DayOfWeek.Sunday, next.DayOfWeek);
            Assert.True(
                next > date,
                $"FutureDate({ahead + 1}) must stay after FutureDate({ahead}); both landed on {date:yyyy-MM-dd}");
        }
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
            SlotId = slot.Id,
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
