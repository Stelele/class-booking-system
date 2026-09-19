using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
        Assert.Equal(HttpStatusCode.OK, req.StatusCode);

        var code = ApiFactory.LastCode; // captured by fake ICodeSender
        Assert.Matches(@"^\d{6}$", code);

        var verify = await client.PostAsJsonAsync("/api/auth/verify",
            new { email = "studenta@example.com", code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var me = await client.GetFromJsonAsync<UserMe>("/api/auth/me");
        Assert.Equal("Student A", me!.Name);
        Assert.Equal("Student", me.Role);

        await client.PostAsync("/api/auth/logout", null);
        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Wrong_code_is_rejected()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/request-code", new { email = "studentb@example.com" });

        var verify = await client.PostAsJsonAsync("/api/auth/verify",
            new { email = "studentb@example.com", code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    public sealed record UserMe(Guid Id, string Name, string Email, string Role);
}
