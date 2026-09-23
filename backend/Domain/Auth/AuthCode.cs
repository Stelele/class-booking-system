namespace Domain.Auth;

public sealed class AuthCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
