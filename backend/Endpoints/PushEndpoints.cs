using System.Security.Claims;
using Application.Abstractions;
using Domain.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Endpoints;

/// <summary>
/// Registering a push subscription is an authenticated request to store an
/// arbitrary https URL that the server will later POST to. Without a host
/// allowlist that is a server-side request forgery primitive, so the endpoint
/// is restricted to the push services browsers actually hand out.
/// </summary>
public static class PushEndpoints
{
    private static readonly string[] AllowedPushHosts =
    [
        "fcm.googleapis.com",          // Chrome, Edge, Android
        "push.services.mozilla.com",   // Firefox
        "notify.windows.com",          // Windows / WNS
        "push.apple.com",              // Safari on iOS and macOS
    ];

    public sealed record SubscribeRequest(
        string? Endpoint, string? P256Dh, string? Auth, bool IsIos = false);

    public static IEndpointRouteBuilder MapPush(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications/push-key", (IConfiguration config) =>
        {
            // Read from configuration rather than Infrastructure.PushOptions to
            // keep the Endpoints layer free of an Infrastructure dependency.
            var key = config["Push:VapidPublicKey"];
            return string.IsNullOrEmpty(key)
                ? Results.Ok(new { enabled = false, vapidPublicKey = (string?)null })
                : Results.Ok(new { enabled = true, vapidPublicKey = key });
        }).RequireAuthorization();

        app.MapPost("/api/notifications/push-subscription", async (
            HttpContext http,
            SubscribeRequest req,
            IAppDbContext db,
            CancellationToken ct) =>
        {
            if (!TryValidate(req, out var endpoint, out var p256dh, out var auth, out var problem))
                return Results.BadRequest(new { error = problem });

            var userId = CurrentUser(http);

            // Re-subscribing (new key material, same endpoint) refreshes rather
            // than duplicates.
            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
            if (existing is not null)
            {
                existing.UserId = userId;
                existing.P256Dh = p256dh;
                existing.Auth = auth;
                existing.IsIos = req.IsIos;
            }
            else
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    UserId = userId, Endpoint = endpoint, P256Dh = p256dh, Auth = auth,
                    IsIos = req.IsIos,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { registered = true });
        }).RequireAuthorization();

        app.MapDelete("/api/notifications/push-subscription", async (
            HttpContext http,
            IAppDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUser(http);
            var mine = await db.PushSubscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync(ct);
            db.PushSubscriptions.RemoveRange(mine);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = mine.Count });
        }).RequireAuthorization();

        return app;
    }

    private static bool TryValidate(
        SubscribeRequest req,
        out string endpoint,
        out string p256dh,
        out string auth,
        out string problem)
    {
        endpoint = p256dh = auth = "";
        problem = "";

        if (string.IsNullOrWhiteSpace(req.Endpoint) ||
            string.IsNullOrWhiteSpace(req.P256Dh) ||
            string.IsNullOrWhiteSpace(req.Auth))
        {
            problem = "endpoint, keys.p256dh and keys.auth are all required";
            return false;
        }

        if (!Uri.TryCreate(req.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            problem = "endpoint must be an absolute https URL";
            return false;
        }

        var host = uri.Host;
        if (!AllowedPushHosts.Any(h =>
                host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
        {
            problem = "endpoint is not a recognised push service";
            return false;
        }

        if (req.Endpoint.Length > 500 || req.P256Dh.Length > 100 || req.Auth.Length > 50)
        {
            problem = "endpoint or key material is too long";
            return false;
        }

        endpoint = req.Endpoint;
        p256dh = req.P256Dh;
        auth = req.Auth;
        return true;
    }

    private static Guid CurrentUser(HttpContext http) =>
        Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
