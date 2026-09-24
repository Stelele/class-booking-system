using Application.Abstractions;
using Application.Backups;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Backups;

/// Backup every 15 minutes, aligned to :00/:15/:30/:45 UTC.
/// Startup restore lives in MigrateAndSeedAsync (must run before the first migration).
public sealed class BackupWorker(IBackupService backups, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = BackupSchedule.NextRunUtc(now);
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
