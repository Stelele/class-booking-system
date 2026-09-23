namespace Domain.Backups;

public sealed class BackupLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime RanAtUtc { get; set; } = DateTime.UtcNow;
    public required string ObjectKey { get; set; }
    public long SizeBytes { get; set; }
}
