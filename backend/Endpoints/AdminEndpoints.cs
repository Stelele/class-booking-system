using Application.Abstractions;
using Application.BlockedDays;
using Application.Users;

namespace Endpoints;

public static class AdminEndpoints
{
    public sealed record BlockRequest(DateOnly Date, string? Reason);
    public sealed record UpdateUserRequest(string Name, string Email, string? Phone);

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

        group.MapGet("/users", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListUsersQuery(), ct)));

        group.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest req, ISender sender, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new UpdateUserCommand(id, req.Name, req.Email, req.Phone), ct));
            }
            catch (UserNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (UserException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
