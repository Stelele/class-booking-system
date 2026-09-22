using Booking.Application.Abstractions;
using Booking.Application.Bookings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Infrastructure.Google;

public sealed class GoogleAccountConnector(
    GoogleOAuthClient oauth,
    IOptions<GoogleOAuthSettings> opts,
    GoogleTokenCrypto crypto,
    IGoogleTokenStore store,
    IHttpClientFactory httpFactory,
    ILogger<GoogleAccountConnector> log) : IGoogleAccountConnector
{
    public async Task ConnectAsync(string code, Guid userId, CancellationToken ct)
    {
        var settings = opts.Value;
        var tokens = await oauth.ExchangeCodeAsync(
            code, settings.ClientId, settings.ClientSecret, settings.RedirectUri, ct);
        await RevokeExistingAsync(ct);
        if (tokens.RefreshToken is null)
            throw new BookingException("Google did not return a refresh token — retry the connect flow.");
        var encrypted = crypto.Encrypt(tokens.RefreshToken);
        await store.SaveAsync(
            new GoogleTokenData(encrypted, tokens.AccessToken, tokens.ExpiryUtc, false), userId, ct);
        log.LogInformation("Google account connected for user {UserId}.", userId);
    }

    private async Task RevokeExistingAsync(CancellationToken ct)
    {
        try
        {
            var existing = await store.GetAsync(ct);
            if (existing is null || string.IsNullOrEmpty(existing.RefreshTokenEncrypted)) return;
            var refreshToken = crypto.Decrypt(existing.RefreshTokenEncrypted);
            if (string.IsNullOrEmpty(refreshToken)) return;
            var http = httpFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
            });
            await http.PostAsync("https://oauth2.googleapis.com/revoke", content, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Best-effort revoke of previous Google refresh token failed; continuing connect flow.");
        }
    }
}
