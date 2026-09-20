using Booking.Application.Abstractions;
using Booking.Infrastructure.Auth;
using Booking.Infrastructure.Backups;
using Booking.Infrastructure.Identity;
using Booking.Infrastructure.Meet;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var dbPath = config["App:DbPath"] ?? "data/booking.db";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        var conn = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
        services.AddSingleton(conn);
        services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IMeetLinkProvider, FixedLinkMeetProvider>();
        services.AddScoped<ICodeSender, EmailCodeSender>();
        if (!string.IsNullOrEmpty(config["R2:Bucket"]))
        {
            services.AddSingleton<IBackupService, R2BackupService>();
            services.AddHostedService<BackupWorker>();
        }
        else
        {
            services.AddSingleton<IBackupService, NullBackupService>();
        }
        return services;
    }

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        // restore BEFORE migrating: on a fresh container the DB file must come from R2,
        // otherwise migration creates an empty DB and the nightly backup buries the real one
        var dbPath = app.Configuration["App:DbPath"] ?? "data/booking.db";
        if (!File.Exists(dbPath) && !string.IsNullOrEmpty(app.Configuration["R2:Bucket"]))
        {
            try
            {
                var backups = app.Services.GetRequiredService<IBackupService>();
                var restore = backups.TryRestoreAsync();
                var done = await Task.WhenAny(restore, Task.Delay(TimeSpan.FromSeconds(30)));
                if (done != restore)
                    app.Logger.LogWarning("Startup restore timed out; booting on a fresh DB.");
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Startup restore failed; booting on a fresh DB.");
            }
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
    }
}
