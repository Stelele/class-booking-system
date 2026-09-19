using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Backups;

/// Nightly backup at 00:00 UTC (02:00 Africa/Harare — CAT is UTC+2 year-round).
public sealed class BackupWorker(
    IBackupService backups, IConfiguration config, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        RestoreIfEmpty();
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

    private void RestoreIfEmpty()
    {
        var dbPath = config["App:DbPath"] ?? "data/booking.db";
        if (!File.Exists(dbPath))
        {
            try { backups.RestoreLatestAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { log.LogWarning(ex, "No R2 backup restored; starting fresh DB."); }
        }
    }
}
