using Booking.Application.Abstractions;
using Booking.Application.BlockedDays;
using Microsoft.EntityFrameworkCore;

namespace Booking.Endpoints;

public static class AdminEndpoints
{
    public sealed record BlockRequest(DateOnly Date, string? Reason);
    public sealed record PhoneRequest(string Email, string Phone);

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("Admin"));

        group.MapPost("/blocked-days", async (BlockRequest req, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new BlockDayCommand(req.Date, req.Reason), ct);
            return Results.Ok(new { ok = true });
        });

        group.MapDelete("/blocked-days/{date}", async (DateOnly date, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UnblockDayCommand(date), ct);
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/backups", async (IBackupService backups, CancellationToken ct) =>
            Results.Ok(new { lastBackupUtc = await backups.LastBackupUtcAsync(ct) }));

        group.MapPost("/backups/run", async (IBackupService backups, CancellationToken ct) =>
        {
            try
            {
                await backups.BackupNowAsync(ct);
                return Results.Ok(new { backedUp = true, lastBackupUtc = await backups.LastBackupUtcAsync(ct) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/backups/restore", async (IBackupService backups, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var swapAndRestart = await backups.RestoreLatestAsync(ct);
                // deterministic: swap only after the 200 has flushed to the client
                http.Response.OnCompleted(() => swapAndRestart());
                return Results.Ok(new { restoring = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/users/phone", async (PhoneRequest req, IAppDbContext db, CancellationToken ct) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(req.Phone ?? "", @"^\+\d{7,15}$"))
                return Results.BadRequest(new { error = "Phone must be E.164, e.g. +447700900123." });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower(), ct);
            if (user is null) return Results.NotFound(new { error = "No such user." });
            user.PhoneE164 = req.Phone;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        });

        return app;
    }
}
