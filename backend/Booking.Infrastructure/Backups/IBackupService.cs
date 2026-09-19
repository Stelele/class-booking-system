namespace Booking.Infrastructure.Backups;

public interface IBackupService
{
    Task BackupNowAsync(CancellationToken ct = default);
    Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default);
    Task RestoreLatestAsync(CancellationToken ct = default);
}
