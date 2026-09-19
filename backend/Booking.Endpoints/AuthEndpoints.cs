using System.Security.Claims;
using Booking.Application.Abstractions;
using Booking.Application.Auth;
using Booking.Application.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Booking.Endpoints;

public static class AuthEndpoints
{
    public sealed record RequestCodeRequest(string Email);
    public sealed record VerifyCodeRequest(string Email, string Code);

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
            UserDto user;
            try
            {
                user = await sender.Send(new VerifyCodeCommand(req.Email, req.Code));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }

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
}
