using Application.Abstractions;
using Application.Bookings;
using Microsoft.Extensions.Options;

namespace Application.Auth;

public sealed class BeginGoogleOAuthQueryHandler(
    IOptions<GoogleOAuthSettings> opts, IGoogleOAuthStateStore states, ICurrentUser user)
    : IQueryHandler<BeginGoogleOAuthQuery, string>
{
    public Task<string> Handle(BeginGoogleOAuthQuery q, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        var settings = opts.Value;
        if (string.IsNullOrEmpty(settings.ClientId)) throw new BookingException("Google is not configured.");
        var state = states.Issue();
        var url = "https://accounts.google.com/o/oauth2/v2/auth?"
            + $"client_id={Uri.EscapeDataString(settings.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(settings.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events")}"
            + "&access_type=offline&prompt=consent"
            + $"&state={Uri.EscapeDataString(state)}";
        return Task.FromResult(url);
    }
}
