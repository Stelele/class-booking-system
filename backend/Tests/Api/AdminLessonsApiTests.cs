using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Api;

/// <summary>
/// The teacher's lessons list. The teacher has no bookings of their own, so
/// without this the join link for a booked day is visible nowhere in the UI.
/// </summary>
[Collection("Api")]
public class AdminLessonsApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    public AdminLessonsApiTests(ApiFactory factory) => _factory = factory;

    // The "Api" collection shares one database, so every booking made here is
    // cancelled again in DisposeAsync rather than left for other tests to trip on.
    private List<Guid> _madeBookings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        foreach (var id in _madeBookings)
        {
            var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
            if (booking is null) continue;
            booking.Status = BookingStatus.Cancelled;
        }
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/request-code", new { email });
        var res = await client.PostAsJsonAsync("/api/auth/verify", new { email, code = ApiFactory.LastCode });
        res.EnsureSuccessStatusCode();
        return client;
    }

    private static DateOnly FutureThursday(int weeksAhead)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(7 * weeksAhead);
        while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1);
        return d;
    }

    private async Task<BookingDto> BookAsync(HttpClient student, DateOnly date)
    {
        var res = await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<BookingDto>(BookingApiTests.ApiJson);
        _madeBookings.Add(dto!.Id);
        return dto;
    }

    [Fact]
    public async Task Admin_lists_the_students_lesson_with_its_link()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday(20);
        var booking = await BookAsync(student, date);
        var admin = await LoginAsync("teacher@example.com");

        var lessons = await admin.GetFromJsonAsync<List<TeacherLessonDto>>(
            "/api/admin/bookings", BookingApiTests.ApiJson);

        var lesson = Assert.Single(lessons!, l => l.Date == date);
        Assert.Equal("Student A", Assert.Single(lesson.StudentNames));
        Assert.Equal(1, lesson.StudentCount);
        Assert.False(lesson.IsCombined);
        Assert.Equal("20:30", lesson.LocalTime);
        Assert.Equal(booking.MeetLink, lesson.MeetLink);
        Assert.NotNull(lesson.MeetLink);
    }

    [Fact]
    public async Task Combined_lesson_reports_both_students()
    {
        var date = FutureThursday(21);
        var a = await LoginAsync("studenta@example.com");
        var b = await LoginAsync("studentb@example.com");
        await BookAsync(a, date);
        await BookAsync(b, date);
        var admin = await LoginAsync("teacher@example.com");

        var lessons = await admin.GetFromJsonAsync<List<TeacherLessonDto>>(
            "/api/admin/bookings", BookingApiTests.ApiJson);

        var lesson = Assert.Single(lessons!, l => l.Date == date);
        Assert.Equal(2, lesson.StudentCount);
        Assert.True(lesson.IsCombined);
    }

    // The round trip the buttons depend on: the id the page shows must be the id
    // the admin is allowed to act on.
    [Fact]
    public async Task Admin_can_cancel_a_students_booking_using_the_listed_id()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday(23);
        var booking = await BookAsync(student, date);
        var admin = await LoginAsync("teacher@example.com");

        var lessons = await admin.GetFromJsonAsync<List<TeacherLessonDto>>(
            "/api/admin/bookings", BookingApiTests.ApiJson);
        var listed = Assert.Single(lessons!, l => l.Date == date);
        var listedId = Assert.Single(listed.Students).BookingId;

        var res = await admin.DeleteAsync($"/api/bookings/{listedId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var after = await admin.GetFromJsonAsync<List<TeacherLessonDto>>(
            "/api/admin/bookings", BookingApiTests.ApiJson);
        Assert.DoesNotContain(after!, l => l.Date == date);
        Assert.NotEqual(Guid.Empty, booking.Id); // sanity: the seeded booking existed
    }

    [Fact]
    public async Task Cancelled_booking_leaves_the_teacher_list()
    {
        var student = await LoginAsync("studenta@example.com");
        var date = FutureThursday(22);
        var booking = await BookAsync(student, date);
        var admin = await LoginAsync("teacher@example.com");

        var cancel = await student.DeleteAsync($"/api/bookings/{booking.Id}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var lessons = await admin.GetFromJsonAsync<List<TeacherLessonDto>>(
            "/api/admin/bookings", BookingApiTests.ApiJson);

        Assert.DoesNotContain(lessons!, l => l.Date == date);
    }

    // The list carries other students' names and the meeting URL, so it must be
    // closed to students even though every logged-in user can see the shared calendar.
    [Fact]
    public async Task Student_is_forbidden()
    {
        var student = await LoginAsync("studenta@example.com");

        var res = await student.GetAsync("/api/admin/bookings");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/api/admin/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}