namespace Booking.Domain.Auth;

public sealed class GoogleToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string RefreshTokenEncrypted { get; set; }
    public string? AccessToken { get; set; }
    public DateTime ExpiryUtc { get; set; }
    public required string Scope { get; set; }
    public bool NeedsReconnect { get; set; }
}