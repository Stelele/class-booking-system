namespace Application.Abstractions;

public interface IBackupService
{
    Task BackupNowAsync(CancellationToken ct = default);
    Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default);

    /// Stages the latest snapshot and returns a swap action to run once the caller's
    /// response has flushed (the swap ends the process).
    Task<Func<Task>> RestoreLatestAsync(CancellationToken ct = default);

    /// Startup path: stage + swap in one step (no process exit — boot continues on the
    /// restored DB). Returns true when a snapshot was restored.
    Task<bool> TryRestoreAsync(CancellationToken ct = default);
}
