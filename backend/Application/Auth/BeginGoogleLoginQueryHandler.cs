using Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Application.Auth;

public sealed class BeginGoogleLoginQueryHandler(
    IOptions<GoogleOAuthSettings> opts, IGoogleOAuthStateStore states)
    : IQueryHandler<BeginGoogleLoginQuery, string>
{
    public async Task<string> Handle(BeginGoogleLoginQuery q, CancellationToken ct)
    {
        var settings = opts.Value;
        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.LoginRedirectUri))
            throw new AuthException("Google login is not configured.");
        var state = states.Issue("login", q.Binding);
        var url = "https://accounts.google.com/o/oauth2/v2/auth?"
            + $"client_id={Uri.EscapeDataString(settings.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(settings.LoginRedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(GoogleLoginScopes.OpenIdEmailProfile)}"
            + "&prompt=select_account"
            + $"&state={Uri.EscapeDataString(state)}";
        return await Task.FromResult(url);
    }
}
