using Booking.Application.Abstractions;
using Booking.Application.BlockedDays;
using Booking.Infrastructure.Backups;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Endpoints;

public static class AdminEndpoints
{
    public sealed record BlockRequest(DateOnly Date, string? Reason);

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("Admin"));

        group.MapPost("/blocked-days", async (BlockRequest req, ISender sender) =>
        {
            await sender.Send(new BlockDayCommand(req.Date, req.Reason));
            return Results.Ok(new { ok = true });
        });

        group.MapDelete("/blocked-days/{date}", async (DateOnly date, ISender sender) =>
        {
            await sender.Send(new UnblockDayCommand(date));
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/backups", async ([FromServices] IBackupService? backups) =>
            Results.Ok(new { lastBackupUtc = backups is null ? null : await backups.LastBackupUtcAsync() }));

        group.MapPost("/backups/restore", async ([FromServices] IBackupService? backups) =>
        {
            if (backups is null) return Results.BadRequest(new { error = "Backups not configured" });
            await backups.RestoreLatestAsync();
            return Results.Ok(new { restoring = true });
        });

        return app;
    }
}
