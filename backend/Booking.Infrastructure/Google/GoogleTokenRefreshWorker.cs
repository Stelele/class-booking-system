using Booking.Application.Abstractions;
using Booking.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                using var scope = scopes.CreateScope();
                var sp = scope.ServiceProvider;
                var store = sp.GetRequiredService<IGoogleTokenStore>();
                var token = await store.GetAsync(ct);
                if (token is not null && !token.NeedsReconnect && token.ExpiryUtc < DateTime.UtcNow.AddHours(36))
                {
                    var oauth = sp.GetRequiredService<GoogleOAuthClient>();
                    var opts = sp.GetRequiredService<IOptions<GoogleOAuthSettings>>().Value;
                    var crypto = sp.GetRequiredService<GoogleTokenCrypto>();
                    var refreshed = await oauth.RefreshAsync(
                        crypto.Decrypt(token.RefreshTokenEncrypted), opts.ClientId, opts.ClientSecret, ct);
                    await store.SaveAsync(token with { AccessToken = refreshed.AccessToken, ExpiryUtc = refreshed.ExpiryUtc }, Guid.Empty, ct);
                    log.LogInformation("Google access token refreshed.");
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                try
                {
                    using var scope2 = scopes.CreateScope();
                    await scope2.ServiceProvider.GetRequiredService<IGoogleTokenStore>().FlagReconnectAsync(ct);
                }
                catch (Exception inner) { log.LogError(inner, "Failed to flag Google reconnect."); }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Google token refresh worker failed."); }
            try { await Task.Delay(TimeSpan.FromHours(12), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
