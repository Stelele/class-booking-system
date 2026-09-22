using Booking.Application.Abstractions;
using Booking.Application.Auth;

namespace Booking.Endpoints;

public static class GoogleAuthEndpoints
{
    public static IEndpointRouteBuilder MapGoogleAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/google/start", async (ISender sender) =>
        {
            var url = await sender.Send(new BeginGoogleOAuthQuery());
            return Results.Redirect(url);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/auth/google/callback", async (string? code, string? state, ISender sender) =>
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Results.Redirect("/admin?google=error");
            try
            {
                await sender.Send(new CompleteGoogleOAuthCommand(code, state));
                return Results.Redirect("/admin?google=connected");
            }
            catch (Exception)
            {
                return Results.Redirect("/admin?google=error");
            }
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/admin/google/status", async (ISender sender) =>
            Results.Ok(await sender.Send(new GetGoogleStatusQuery())))
           .RequireAuthorization(p => p.RequireRole("Admin"));

        return app;
    }
}
