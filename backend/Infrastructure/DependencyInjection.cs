using Application.Abstractions;
using Infrastructure.Auth;
using Infrastructure.Google;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Infrastructure.Backups;
using Infrastructure.Identity;
using Infrastructure.Meet;
using Infrastructure.Persistence;
using Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Infrastructure;

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
            var tokenKeyB64 = config["Google:TokenKey"];
            if (string.IsNullOrEmpty(tokenKeyB64))
                throw new InvalidOperationException("Google:TokenKey is not configured.");
            byte[] tokenKey;
            try { tokenKey = Convert.FromBase64String(tokenKeyB64); }
            catch (FormatException ex) { throw new InvalidOperationException("Google:TokenKey is not valid base64.", ex); }
            if (tokenKey.Length != 32)
                throw new InvalidOperationException("Google:TokenKey must decode to exactly 32 bytes.");
            services.AddSingleton(new GoogleTokenCrypto(tokenKey));
        }
        else
        {
            services.AddScoped<IMeetLinkProvider, FixedLinkMeetProvider>();
            services.AddScoped<IMeetEventSync, FixedLinkEventSync>();
            // Fixed mode stores no real Google tokens, but the always-registered
            // connector still requires a resolvable crypto service. Lazy factory
            // with environment gate: dev/test get a deterministic key, production
            // fails LOUDLY rather than encrypting real tokens with a known key.
            services.AddSingleton(_ =>
            {
                var fallbackB64 = config["Google:TokenKey"];
                var isDev = string.Equals(config["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)
                    || config["E2E"] == "true";
                byte[] fallbackKey;
                if (string.IsNullOrEmpty(fallbackB64))
                {
                    if (!isDev) throw new InvalidOperationException("Google:TokenKey is not configured.");
                    fallbackKey = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("dev-only-google-token-key"));
                }
                else
                {
                    try { fallbackKey = Convert.FromBase64String(fallbackB64); }
                    catch (FormatException ex) { throw new InvalidOperationException("Google:TokenKey is not valid base64.", ex); }
                }
                return new GoogleTokenCrypto(fallbackKey);
            });
        }
        // Google OAuth wiring: connect flow + status endpoint.
        services.Configure<GoogleOAuthSettings>(config.GetSection("Google"));
        services.AddSingleton<IGoogleOAuthStateStore, GoogleOAuthStateStore>();
        // Single-instance only; replace with distributed cache if ever scaling horizontally
        services.AddScoped<IGoogleAccountConnector, GoogleAccountConnector>();
        services.AddScoped<IGoogleLoginService, GoogleLoginService>();
        services.AddScoped<IGoogleTokenStore, EfGoogleTokenStore>();
        // Typed token client (base address is the stable Google endpoint).
        services.AddHttpClient<GoogleOAuthClient>(c =>
            c.BaseAddress = new Uri("https://oauth2.googleapis.com/"));
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
        services.Configure<TwilioOptions>(config.GetSection("Twilio"));
        services.AddHttpClient("Twilio", c => c.BaseAddress = new Uri("https://api.twilio.com/"));
        services.AddScoped<ITwilioSender>(sp =>
            string.IsNullOrEmpty(config["Twilio:AccountSid"])
                ? new NullTwilioSender(sp.GetRequiredService<ILogger<NullTwilioSender>>())
                : new TwilioWhatsAppSender(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Twilio"),
                    sp.GetRequiredService<IOptions<TwilioOptions>>(),
                    sp.GetRequiredService<ILogger<TwilioWhatsAppSender>>()));
        services.AddHostedService<ReminderService>();
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
