namespace Application.Abstractions;

public sealed record GoogleTokenData(string RefreshTokenEncrypted, string AccessToken, DateTime ExpiryUtc, bool NeedsReconnect);

public interface IGoogleTokenStore
{
    Task<GoogleTokenData?> GetAsync(CancellationToken ct);
    Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct);
    Task FlagReconnectAsync(CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
}
