using System.Security.Cryptography;
using Application.Abstractions;
using Application.Bookings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Google;

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

    public async Task<bool> DisconnectAsync(CancellationToken ct)
    {
        var remoteRevoked = false;
        try
        {
            var existing = await store.GetAsync(ct);
            if (existing is null) return true;

            var refreshToken = crypto.Decrypt(existing.RefreshTokenEncrypted);
            if (string.IsNullOrEmpty(refreshToken))
                throw new CryptographicException("Stored Google refresh token is empty.");

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
            });
            using var http = httpFactory.CreateClient();
            using var response = await http.PostAsync(
                "https://oauth2.googleapis.com/revoke", content, ct);
            remoteRevoked = response.IsSuccessStatusCode;
            if (!remoteRevoked)
                log.LogWarning(
                    "Google token revocation returned {StatusCode}; deleting the local token.",
                    (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google token revocation failed; deleting the local token.");
        }
        finally
        {
            await store.DeleteAsync(CancellationToken.None);
        }

        return remoteRevoked;
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
