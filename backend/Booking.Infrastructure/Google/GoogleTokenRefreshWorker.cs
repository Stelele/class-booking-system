using Booking.Application.Abstractions;
using Booking.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Booking.Infrastructure.Google;

/// Refreshes the Google access token well before expiry so idle weeks don't
/// hit expiry; surfaces invalid_grant early via NeedsReconnect.
public sealed class GoogleTokenRefreshWorker(
    IServiceScopeFactory scopes, ILogger<GoogleTokenRefreshWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } // let boot finish
        catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(ct);
            }
            catch (HttpRequestException ex) when (IsAuthLoss(ex))
            {
                await FlagReconnectAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Google token refresh worker failed."); }
            try { await Task.Delay(TimeSpan.FromHours(12), ct); }
            catch (OperationCanceledException) { break; }
        }

        // Single pass; any early return skips the remaining work and the loop
        // falls through to the scheduled 12h delay above (never a hot loop).
        async Task RefreshOnceAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var store = sp.GetRequiredService<IGoogleTokenStore>();
            var token = await store.GetAsync(ct);
            if (token is null || token.NeedsReconnect || token.ExpiryUtc >= DateTime.UtcNow.AddHours(36))
                return; // nothing due
            var oauth = sp.GetRequiredService<GoogleOAuthClient>();
            var opts = sp.GetRequiredService<IOptions<GoogleOAuthSettings>>().Value;
            var crypto = sp.GetRequiredService<GoogleTokenCrypto>();
            string refreshToken;
            try
            {
                refreshToken = crypto.Decrypt(token.RefreshTokenEncrypted);
            }
            catch (CryptographicException cex)
            {
                // Key rotated or payload corrupt: the stored secret is unusable,
                // so the teacher must reconnect — same as an invalid_grant.
                log.LogWarning(cex, "Google refresh token failed to decrypt; flagging reconnect.");
                await FlagReconnectAsync();
                return;
            }
            var refreshed = await oauth.RefreshAsync(
                refreshToken, opts.ClientId, opts.ClientSecret, ct);
            await store.SaveAsync(token with { AccessToken = refreshed.AccessToken, ExpiryUtc = refreshed.ExpiryUtc }, Guid.Empty, ct);
            log.LogInformation("Google access token refreshed.");
        }

        async Task FlagReconnectAsync()
        {
            try
            {
                using var flagScope = scopes.CreateScope();
                await flagScope.ServiceProvider.GetRequiredService<IGoogleTokenStore>().FlagReconnectAsync(ct);
            }
            catch (Exception inner) { log.LogError(inner, "Failed to flag Google reconnect."); }
        }
    }

    // Auth loss only: 401/403, or 400 carrying invalid_grant (revoked/expired
    // grant). Other 400s and transient failures log only — no reconnect flag.
    private static bool IsAuthLoss(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
        || (ex.StatusCode is System.Net.HttpStatusCode.BadRequest && ex.Message.Contains("invalid_grant"));
}
