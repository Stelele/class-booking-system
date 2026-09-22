using Booking.Application.Abstractions;
using Booking.Infrastructure.Auth;
using Booking.Infrastructure.Google;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Booking.Infrastructure.Backups;
using Booking.Infrastructure.Identity;
using Booking.Infrastructure.Meet;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var dbPath = config["App:DbPath"] ?? "data/booking.db";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        // POOLED connections, one per DbContext instance — a shared singleton connection
        // is not thread-safe and breaks under concurrent requests ("reader is closed")
        var connString = $"Data Source={dbPath};Foreign Keys=True";
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddHttpClient("Google", c => c.BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/"));
        services.AddScoped<FixedLinkMeetProvider>();
        var googleEnabled = (config["App:Meet:Provider"] ?? "fixed").Equals("google", StringComparison.OrdinalIgnoreCase);
        if (googleEnabled)
        {
            services.AddScoped<IMeetLinkProvider, GoogleCalendarProvider>();
            services.AddScoped<IMeetEventSync, GoogleCalendarProvider>();
            services.AddHostedService<GoogleTokenRefreshWorker>();
        }
        else
        {
            services.AddScoped<IMeetLinkProvider, FixedLinkMeetProvider>();
            services.AddScoped<IMeetEventSync, FixedLinkEventSync>();
        }
        // Task 6 (Slice 2A): minimal Google OAuth wiring so the connect flow + status
        // endpoint resolve. Task 7 owns the rest (calendar provider/sync, worker,
        // settings validation) but keeps/extends these three lines.
        services.Configure<GoogleOAuthSettings>(config.GetSection("Google"));
        services.AddSingleton<IGoogleOAuthStateStore, GoogleOAuthStateStore>();
        // Single-instance only; replace with distributed cache if ever scaling horizontally
        services.AddScoped<IGoogleAccountConnector, GoogleAccountConnector>();
        services.AddScoped<IGoogleTokenStore, EfGoogleTokenStore>();
        // Typed token client (Task 7 keeps; base address is the stable Google endpoint).
        services.AddHttpClient<GoogleOAuthClient>(c =>
            c.BaseAddress = new Uri("https://oauth2.googleapis.com/"));
        // Refresh-token encryption (Task 7 keeps; dev/test fallback is a deterministic
        // local key — real deployments must set Google:TokenKey, see Task 7 validation).
        services.AddSingleton<GoogleTokenCrypto>(_ =>
        {
            var b64 = config["Google:TokenKey"];
            var isDev = string.Equals(config["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)
                || config["E2E"] == "true";
            byte[] key = !string.IsNullOrEmpty(b64) ? Convert.FromBase64String(b64)
                : isDev ? System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("dev-only-google-token-key"))
                : throw new InvalidOperationException("Google:TokenKey is not configured.");
            return new GoogleTokenCrypto(key);
        });
        // Resend HTTPS API when configured (works behind DO's SMTP port blocks);
        // classic SMTP otherwise (Gmail etc.)
        services.Configure<EmailHttpOptions>(config.GetSection("Email:Http"));
        services.AddHttpClient("Resend", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<EmailHttpOptions>>().Value;
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        });
        if (!string.IsNullOrEmpty(config["Email:Http:ApiKey"]))
            services.AddScoped<ICodeSender, ResendEmailSender>();
        else
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
        // E2E hook: capture login codes in-process so tests can read them via /api/test/latest-code
        if (config["E2E"] == "true")
            services.Replace(ServiceDescriptor.Scoped<ICodeSender, E2eCodeSender>());
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
                // token fires at the deadline: in-flight S3 calls abort and a late stage
                // never swaps (TryRestoreAsync checks the token before AtomicSwap)
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var restore = backups.TryRestoreAsync(timeoutCts.Token);
                var done = await Task.WhenAny(restore, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
                if (done == restore)
                    await restore; // observe failures into the catch below
                else
                    app.Logger.LogWarning("Startup restore timed out; booting on a fresh DB.");
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Startup restore failed; booting on a fresh DB.");
            }
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // WAL: readers don't block the writer — cheap concurrency for a 3-user app
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.MigrateAsync();
        await Seeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
    }
}
