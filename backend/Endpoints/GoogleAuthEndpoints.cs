using Application.Abstractions;
using Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Endpoints;

public static class GoogleAuthEndpoints
{
    public static IEndpointRouteBuilder MapGoogleAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/google/start", async (ISender sender) =>
        {
            var url = await sender.Send(new BeginGoogleOAuthQuery());
            return Results.Redirect(url);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/auth/google/callback", async (string? code, string? state, ISender sender, ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("GoogleAuth");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                log.LogWarning("Google OAuth callback missing code or state.");
                return Results.Redirect("/admin?google=error");
            }
            try
            {
                await sender.Send(new CompleteGoogleOAuthCommand(code, state));
                return Results.Redirect("/admin?google=connected");
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Google OAuth callback failed.");
                return Results.Redirect("/admin?google=error");
            }
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Includes the active Meet provider: "connected" is a true statement
        // about the account but says nothing about whether bookings create
        // calendar events, which is gated on App:Meet:Provider. Returning both
        // makes a mismatch visible instead of silently producing fixed links.
        app.MapGet("/api/admin/google/status", async (ISender sender, IConfiguration config) =>
        {
            var status = await sender.Send(new GetGoogleStatusQuery());
            var provider = (config["App:Meet:Provider"] ?? "fixed").ToLowerInvariant();
            return Results.Ok(new
            {
                status.Connected,
                status.NeedsReconnect,
                MeetProvider = provider,
                // false when Google is connected but events will not be created.
                EventsEnabled = provider == "google",
            });
        })
           .RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapDelete("/api/admin/google", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new DisconnectGoogleCommand(), ct)))
           .RequireAuthorization(p => p.RequireRole("Admin"));

        return app;
    }
}
