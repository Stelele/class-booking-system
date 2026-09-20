using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Backups;

/// Nightly backup at 00:00 UTC (02:00 Africa/Harare — CAT is UTC+2 year-round).
/// Startup restore lives in MigrateAndSeedAsync (must run before the first migration).
public sealed class BackupWorker(
    IBackupService backups, IConfiguration config, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1); // next 00:00 UTC
            try
            {
                await Task.Delay(next - now, ct);
                await backups.BackupNowAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Backup failed; retrying in 1h");
                try { await Task.Delay(TimeSpan.FromHours(1), ct); }
                catch (OperationCanceledException) { }
            }
        }
    }
}
