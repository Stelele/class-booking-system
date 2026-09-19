using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Booking.Application.Abstractions;
using Booking.Domain.Backups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Booking.Infrastructure.Persistence;

namespace Booking.Infrastructure.Backups;

public sealed class R2BackupService(
    IServiceProvider sp, IConfiguration config, ILogger<R2BackupService> log) : IBackupService
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
        using (var transfer = new TransferUtility(Client()))
            await transfer.UploadAsync(tmpGz, config["R2:Bucket"], key, ct);

        var size = new FileInfo(tmpGz).Length;
        File.Delete(tmp);
        File.Delete(tmpGz);

        await using var db = sp.GetRequiredService<AppDbContext>();
        db.BackupLogs.Add(new BackupLog { ObjectKey = key, SizeBytes = size });
        await db.SaveChangesAsync(ct);
        log.LogInformation("R2 backup uploaded: {Key} ({Size} bytes)", key, size);
    }

    public async Task<DateTime?> LastBackupUtcAsync(CancellationToken ct = default)
    {
        await using var db = sp.GetRequiredService<AppDbContext>();
        var last = await db.BackupLogs.OrderByDescending(b => b.RanAtUtc).FirstOrDefaultAsync(ct);
        return last?.RanAtUtc;
    }

    public async Task RestoreLatestAsync(CancellationToken ct = default)
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
        await using (var gz = File.OpenRead(tmpGz))
        await using (var outDb = File.Create(tmp))
        await using (var gunzip = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionMode.Decompress))
            await gunzip.CopyToAsync(outDb, ct);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DbPath))!);
        File.Copy(tmp, DbPath, overwrite: true);
        log.LogWarning("Restored DB from {Key}; restarting.", newest.Key);
        Environment.Exit(0); // container restart policy brings the app back, migration runs on boot
    }
}
