using System.Net;
using System.Net.Http.Json;
using Booking.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Booking.Tests.Api;

[Collection("Api")]
public class AdminPhoneTests
{
    private readonly ApiFactory _factory;
    public AdminPhoneTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/request-code", new { email });
        var res = await client.PostAsJsonAsync("/api/auth/verify", new { email, code = ApiFactory.LastCode });
        res.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Admin_sets_phone_and_it_persists()
    {
        var admin = await LoginAsync("teacher@example.com");

        var res = await admin.PostAsJsonAsync("/api/admin/users/phone",
            new { email = "studenta@example.com", phone = "+447700900123" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "studenta@example.com");
        Assert.Equal("+447700900123", user.PhoneE164);
    }

    [Fact]
    public async Task Student_cannot_set_phones()
    {
        var student = await LoginAsync("studenta@example.com");

        var res = await student.PostAsJsonAsync("/api/admin/users/phone",
            new { email = "studentb@example.com", phone = "+447700900124" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_phone_rejected()
    {
        var admin = await LoginAsync("teacher@example.com");

        foreach (var bad in new[] { "07700900123", "+44", "+44770090012345678901234", "" })
        {
            var res = await admin.PostAsJsonAsync("/api/admin/users/phone",
                new { email = "studentb@example.com", phone = bad });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task Unknown_email_returns_not_found()
    {
        var admin = await LoginAsync("teacher@example.com");

        var res = await admin.PostAsJsonAsync("/api/admin/users/phone",
            new { email = "nobody@example.com", phone = "+447700900125" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
