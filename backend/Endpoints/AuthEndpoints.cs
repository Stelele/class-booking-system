using Application.Abstractions;
using Application.Auth;
using Application.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Endpoints;

public static class AuthEndpoints
{
    public sealed record RequestCodeRequest(string Email);
    public sealed record VerifyCodeRequest(string Email, string Code);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/request-code", async (RequestCodeRequest req, ISender sender) =>
        {
            try
            {
                await sender.Send(new RequestCodeCommand(req.Email));
                return Results.Ok(new { sent = true });
            }
            catch (AuthException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
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

            await AuthCookie.IssueAsync(http, user);
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
