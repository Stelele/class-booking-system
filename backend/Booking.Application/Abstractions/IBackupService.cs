namespace Booking.Application.Abstractions;

public interface IBackupService
{
    Task BackupNowAsync(CancellationToken ct = default);
    Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default);

    /// Stages the latest snapshot and returns a swap action to run once the caller's
    /// response has flushed (the swap ends the process).
    Task<Func<Task>> RestoreLatestAsync(CancellationToken ct = default);
}
