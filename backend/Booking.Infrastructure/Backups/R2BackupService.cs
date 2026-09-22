using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Booking.Application.Abstractions;
using Booking.Domain.Backups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Booking.Infrastructure.Persistence;

namespace Booking.Infrastructure.Backups;

public sealed class R2BackupService(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<R2BackupService> log) : IBackupService
{
    private string DbPath => config["App:DbPath"] ?? "data/booking.db";

    private IAmazonS3 Client()
    {
        var accountId = config["R2:AccountId"] ?? throw new InvalidOperationException("R2:AccountId missing");
        var key = config["R2:AccessKeyId"] ?? throw new InvalidOperationException("R2:AccessKeyId missing");
        var secret = config["R2:SecretAccessKey"] ?? throw new InvalidOperationException("R2:SecretAccessKey missing");
        return new AmazonS3Client(key, secret, new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        });
    }

    public async Task BackupNowAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DbPath))!);
        var tmp = Path.Combine(Path.GetTempPath(), $"booking-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        await using (var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}"))
        await using (var target = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tmp}"))
        {
            await source.OpenAsync(ct);
            await target.OpenAsync(ct);
            source.BackupDatabase(target);
        }

        var tmpGz = tmp + ".gz";
        await using (var input = File.OpenRead(tmp))
        await using (var gz = File.Create(tmpGz))
        await using (var gzip = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.Optimal))
            await input.CopyToAsync(gzip, ct);

        var key = $"backups/{DateTime.UtcNow:yyyy/MM/dd/HHmmss}.db.gz";
        using var s3 = Client();
        // R2 rejects the SDK's default chunked STREAMING-*-TRAILER uploads —
        // payload signing + default checksums must be disabled (same flags as
        // erpnext-dashboard's R2StorageService); backups are tiny so the
        // high-level TransferUtility isn't needed anyway
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = config["R2:Bucket"],
            Key = key,
            FilePath = tmpGz,
            ContentType = "application/gzip",
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        }, ct);

        var size = new FileInfo(tmpGz).Length;
        File.Delete(tmp);
        File.Delete(tmpGz);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BackupLogs.Add(new BackupLog { ObjectKey = key, SizeBytes = size });
        await db.SaveChangesAsync(ct);
        log.LogInformation("R2 backup uploaded: {Key} ({Size} bytes)", key, size);
    }

    public async Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var last = await db.BackupLogs.OrderByDescending(b => b.RanAtUtc).FirstOrDefaultAsync(ct);
        return last?.RanAtUtc;
    }

    private int _restoreGate;

    public async Task<Func<Task>> RestoreLatestAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _restoreGate, 1, 0) != 0)
            throw new InvalidOperationException("A restore is already in progress.");

        try
        {
            var (staged, key) = await StageRestore(ct);
            // The swap runs after the caller's HTTP response has flushed (Response.OnCompleted);
            // it ends the process so the container restarts onto the restored DB.
            return async () =>
            {
                try
                {
                    AtomicSwap(staged);
                    log.LogWarning("Restored DB from {Key}; restarting.", key);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Restore swap failed — restarting on the existing DB");
                }
                finally
                {
                    Environment.Exit(0);
                }
            };
        }
        catch
        {
            // staging failed — release the gate so future restores can proceed;
            // only hold it once a swap is actually armed
            Interlocked.Exchange(ref _restoreGate, 0);
            throw;
        }
    }

    public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _restoreGate, 1, 0) != 0)
            return false; // another restore in flight — boot fresh rather than block
        try
        {
            var (staged, key) = await StageRestore(ct);
            ct.ThrowIfCancellationRequested(); // a timed-out restore must never swap under a live app
            AtomicSwap(staged);
            log.LogWarning("Startup restore from {Key} applied.", key);
            return true;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Startup restore failed; starting fresh DB.");
            return false;
        }
        finally
        {
            // no pending swap on this path — the gate must not stay latched
            Interlocked.Exchange(ref _restoreGate, 0);
        }
    }

    private async Task<(string StagedPath, string Key)> StageRestore(CancellationToken ct)
    {
        using var s3 = Client();
        var list = await s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = config["R2:Bucket"],
            Prefix = "backups/"
        }, ct);
        var newest = list.S3Objects.OrderByDescending(o => o.LastModified).FirstOrDefault()
                     ?? throw new InvalidOperationException("No backups found in R2.");

        var tmpGz = Path.Combine(Path.GetTempPath(), $"restore-{Guid.NewGuid():N}.gz");
        using (var resp = await s3.GetObjectAsync(new GetObjectRequest
               { BucketName = config["R2:Bucket"], Key = newest.Key }, ct))
        await using (var file = File.Create(tmpGz))
            await resp.ResponseStream.CopyToAsync(file, ct);

        var tmp = tmpGz + ".db";
        try
        {
            await using (var gz = File.OpenRead(tmpGz))
            await using (var outDb = File.Create(tmp))
            await using (var gunzip = new GZipStream(gz, CompressionMode.Decompress))
                await gunzip.CopyToAsync(outDb, ct);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("Latest backup snapshot is corrupt.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DbPath))!);
        return (tmp, newest.Key);
    }

    /// Atomic on the same filesystem: a midway failure never leaves a truncated booking.db.
    private void AtomicSwap(string staged)
    {
        var finalTmp = DbPath + ".restore.tmp";
        File.Copy(staged, finalTmp, overwrite: true);
        if (File.Exists(DbPath)) File.Replace(finalTmp, DbPath, null);
        else File.Move(finalTmp, DbPath);
    }
}
