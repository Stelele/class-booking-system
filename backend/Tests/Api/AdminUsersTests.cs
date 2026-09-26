using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Api;

[Collection("Api")]
public class AdminUsersTests : IAsyncLifetime
{
    // mirrors Application.DTOs.AdminUserDto — declared locally so the test
    // compiles (and fails on a real assertion) before that type exists
    internal sealed record AdminUserRow(Guid Id, string Name, string Email, string? Phone, string Role);

    private readonly ApiFactory _factory;
    public AdminUsersTests(ApiFactory factory) => _factory = factory;

    // The "Api" collection shares ONE database and other tests assert on the
    // placeholder names ("Student A" in BookingApiTests), so every mutation
    // this class makes is undone when the test ends.
    private List<(Guid Id, string Name, string Email, string? Phone)> _snapshot = [];

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        _snapshot = await db.Users.AsNoTracking()
            .Select(u => new ValueTuple<Guid, string, string, string?>(u.Id, u.Name, u.Email, u.PhoneE164))
            .ToListAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        foreach (var (id, name, email, phone) in _snapshot)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) continue;
            user.Name = name; user.Email = email; user.PhoneE164 = phone;
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

    private async Task<AdminUserRow> RowForAsync(HttpClient admin, string email) =>
        (await admin.GetFromJsonAsync<List<AdminUserRow>>("/api/admin/users"))!.Single(r => r.Email == email);

    private async Task<HttpResponseMessage> PutAsync(HttpClient admin, Guid id, string name, string email, string? phone) =>
        await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { name, email, phone });

    [Fact]
    public async Task Admin_lists_all_seeded_users()
    {
        var admin = await LoginAsync("teacher@example.com");

        var rows = await admin.GetFromJsonAsync<List<AdminUserRow>>("/api/admin/users");

        Assert.Equal(3, rows!.Count);
        Assert.Equal("Admin", rows.Single(r => r.Email == "teacher@example.com").Role);
        Assert.Contains(rows, r => r.Email == "studenta@example.com");
        Assert.Contains(rows, r => r.Email == "studentb@example.com");
    }

    [Fact]
    public async Task Admin_renames_student_and_shared_calendar_shows_the_new_name()
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studenta@example.com");

        var res = await PutAsync(admin, target.Id, "Alice Moyo", target.Email, null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // the whole point: the shared calendar shows the real name
        var student = await LoginAsync("studenta@example.com");
        var date = FirstBookableThursday();
        await student.PostAsJsonAsync("/api/bookings", new { date = date.ToString("yyyy-MM-dd") });

        var days = await student.GetFromJsonAsync<List<SlotDayDto>>(
            $"/api/slots?year={date.Year}&month={date.Month}", BookingApiTests.ApiJson);
        var names = days!.Single(d => d.Date == date).StudentNames;
        Assert.Contains("Alice Moyo", names);
        Assert.DoesNotContain("Student A", names);
    }

    [Fact]
    public async Task Admin_edits_their_own_row()
    {
        // the "Teacher" placeholder is the admin's own row — renaming self
        // must work, and the session must survive an email change
        var admin = await LoginAsync("teacher@example.com");
        var me = await RowForAsync(admin, "teacher@example.com");

        var res = await PutAsync(admin, me.Id, "Gift Mugweni", me.Email, "+263771234567");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var after = await RowForAsync(admin, "teacher@example.com");
        Assert.Equal("Gift Mugweni", after.Name);
        Assert.Equal("+263771234567", after.Phone);

        // still an admin after the edit — the cookie keys off the id
        var blocked = await admin.PostAsJsonAsync("/api/admin/blocked-days",
            new { date = FirstBookableThursday().AddDays(1).ToString("yyyy-MM-dd"), reason = "trip" });
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_list_users()
    {
        var student = await LoginAsync("studenta@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Student_cannot_edit_users()
    {
        var student = await LoginAsync("studenta@example.com");
        var victim = await LoginAsync("teacher@example.com");
        var teacher = await RowForAsync(victim, "teacher@example.com");

        var res = await PutAsync(student, teacher.Id, "Hacked", teacher.Email, null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_list_users()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/users")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A name that is comfortably and unambiguously longer than the hundred character column limit allows for")]
    public async Task Invalid_name_rejected(string name)
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");

        var res = await PutAsync(admin, target.Id, name, target.Email, null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task Invalid_email_rejected(string email)
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");

        var res = await PutAsync(admin, target.Id, "Bob Chirwa", email, null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Email_already_used_by_another_user_rejected()
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");

        var res = await PutAsync(admin, target.Id, "Bob Chirwa", "studenta@example.com", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // and the rejected edit changed nothing
        Assert.Equal("Student B", (await RowForAsync(admin, "studentb@example.com")).Name);
    }

    [Fact]
    public async Task Email_is_stored_lowercase_so_login_keeps_working()
    {
        // login lookups normalise case, so a mixed-case stored email would
        // lock the user out of their own account
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");

        var res = await PutAsync(admin, target.Id, "Bob Chirwa", "  Bob.Chirwa@Example.COM  ", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("bob.chirwa@example.com", (await RowForAsync(admin, "bob.chirwa@example.com")).Email);

        // the user can still request a code with the new address
        var code = await admin.PostAsJsonAsync("/api/auth/request-code", new { email = "BOB.CHIRWA@example.com" });
        Assert.Equal(HttpStatusCode.OK, code.StatusCode);
    }

    [Theory]
    [InlineData("07700900123")]
    [InlineData("+44")]
    [InlineData("+44770090012345678901234")]
    [InlineData("not-a-number")]
    public async Task Invalid_phone_rejected(string phone)
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");

        var res = await PutAsync(admin, target.Id, "Bob Chirwa", target.Email, phone);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Blank_phone_clears_the_number()
    {
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studentb@example.com");
        await PutAsync(admin, target.Id, "Bob Chirwa", target.Email, "+447700900124");

        var res = await PutAsync(admin, target.Id, "Bob Chirwa", target.Email, "  ");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null((await RowForAsync(admin, target.Email)).Phone);
    }

    [Fact]
    public async Task Unknown_user_returns_not_found()
    {
        var admin = await LoginAsync("teacher@example.com");

        var res = await PutAsync(admin, Guid.NewGuid(), "Nobody", "nobody@example.com", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Me_reflects_a_rename_without_a_fresh_login()
    {
        // the session cookie carries the name for 30 days, so reading it back
        // would show the old name in the header badge until re-login
        var student = await LoginAsync("studenta@example.com");
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studenta@example.com");
        await PutAsync(admin, target.Id, "Alice Moyo", target.Email, null);

        var me = await student.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("Alice Moyo", me!.Name);
    }

    [Fact]
    public async Task Admin_can_change_a_students_email_without_breaking_their_session()
    {
        var student = await LoginAsync("studenta@example.com");
        var admin = await LoginAsync("teacher@example.com");
        var target = await RowForAsync(admin, "studenta@example.com");

        var res = await PutAsync(admin, target.Id, "Alice Moyo", "alice@example.com", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // the cookie keys off the user id, not the email
        var me = await student.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("Alice Moyo", me!.Name);
        Assert.Equal("alice@example.com", me.Email);
    }

    private static DateOnly FirstBookableThursday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(14);
        while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1);
        return d;
    }
}
