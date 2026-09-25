using Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Google;

public sealed class GoogleLoginService(GoogleOAuthClient oauth, IOptions<GoogleOAuthSettings> opts)
    : IGoogleLoginService
{
    public async Task<GoogleLoginIdentity> AuthenticateAsync(string code, CancellationToken ct)
    {
        var settings = opts.Value;
        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ClientSecret)
            || string.IsNullOrEmpty(settings.LoginRedirectUri))
            throw new InvalidOperationException("Google login is not configured.");
        var tokens = await oauth.ExchangeCodeAsync(
            code, settings.ClientId, settings.ClientSecret, settings.LoginRedirectUri, ct);
        return await oauth.GetUserInfoAsync(tokens.AccessToken, ct);
    }
}
