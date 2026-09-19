using System.Net;
using System.Net.Http.Json;
using Booking.Application.DTOs;
using Xunit;

namespace Booking.Tests.Api;

[Collection("Api")]
public class BookingApiTests
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

    private static DateOnly NextSunday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1);
        while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task Student_books_then_sees_it_on_shared_calendar()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday();

        var booking = await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);

        var days = await student.GetFromJsonAsync<List<SlotDayDto>>($"/api/slots?year={date.Year}&month={date.Month}");
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

        var days = await b.GetFromJsonAsync<List<SlotDayDto>>($"/api/slots?year={date.Year}&month={date.Month}");
        Assert.Equal(DayState.Combined, days!.Single(d => d.Date == date).State);
    }

    [Fact]
    public async Task Sunday_rejected_and_blocked_day_rejected()
    {
        var student = await LoginAsync("studenta@example.com");
        var admin = await LoginAsync("teacher@example.com");
        var sunday = NextSunday();
        var thursday = FutureThursday(5);

        var res = await student.PostAsJsonAsync("/api/bookings", new { date = sunday.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Sunday", await res.Content.ReadAsStringAsync());

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
        Assert.NotNull(created);

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
    public async Task Anonymous_cannot_view_and_student_cannot_block()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/slots?year=2026&month=10")).StatusCode);

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

    [Fact]
    public async Task Student_cannot_cancel_others_booking_and_anonymous_ics_rejected()
    {
        var a = await LoginAsync("studenta@example.com");
        var b = await LoginAsync("studentb@example.com");
        var date = FutureThursday(9);
        var created = await (await a.PostAsJsonAsync("/api/bookings",
            new { date = date.ToString("yyyy-MM-dd") })).Content.ReadFromJsonAsync<BookingDto>();
        Assert.NotNull(created);

        var hijack = await b.DeleteAsync($"/api/bookings/{created!.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, hijack.StatusCode);
        Assert.Contains("own bookings", await hijack.Content.ReadAsStringAsync());

        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync($"/api/slots/{date:yyyy-MM-dd}/ics")).StatusCode);
    }

    [Fact]
    public async Task Invalid_month_rejected_and_admin_can_unblock()
    {
        var student = await LoginAsync("studenta@example.com");
        var admin = await LoginAsync("teacher@example.com");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await student.GetAsync("/api/slots?year=2026&month=13")).StatusCode);

        var date = FutureThursday(10);
        await admin.PostAsJsonAsync("/api/admin/blocked-days", new { date = date.ToString("yyyy-MM-dd"), reason = "trip" });
        var blockedRes = await admin.DeleteAsync($"/api/admin/blocked-days/{date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, blockedRes.StatusCode);

        var book = await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, book.StatusCode);
    }
}
