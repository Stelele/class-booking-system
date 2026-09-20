using Booking.Application.Abstractions;

namespace Booking.Infrastructure.Backups;

/// Registered when R2 is not configured (local dev/tests) so the dependency stays required.
public sealed class NullBackupService : IBackupService
{
    public Task BackupNowAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
    public Task<Func<Task>> RestoreLatestAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("Backups are not configured.");
    public Task<bool> TryRestoreAsync(CancellationToken ct = default) => Task.FromResult(false);
}
