namespace Booking.Infrastructure.Google;

public sealed record GoogleTokenData(string RefreshTokenEncrypted, string AccessToken, DateTime ExpiryUtc, bool NeedsReconnect);

public interface IGoogleTokenStore
{
    Task<GoogleTokenData?> GetAsync(CancellationToken ct);
    Task SaveAsync(GoogleTokenData token, CancellationToken ct);
    Task FlagReconnectAsync(CancellationToken ct);
}
