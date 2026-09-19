using Booking.Application.Abstractions;
using Booking.Infrastructure.Backups;
using Booking.Infrastructure.Identity;
using Booking.Infrastructure.Meet;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IMeetLinkProvider, FixedLinkMeetProvider>();
        if (!string.IsNullOrEmpty(config["R2:Bucket"]))
        {
            services.AddSingleton<IBackupService, R2BackupService>();
            services.AddHostedService<BackupWorker>();
        }
        return services;
    }

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
    }
}
