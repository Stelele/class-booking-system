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
