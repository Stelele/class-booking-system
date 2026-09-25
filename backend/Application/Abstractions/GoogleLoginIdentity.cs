namespace Application.Abstractions;

public sealed record GoogleLoginIdentity(
    string Subject, string Email, bool EmailVerified, string Name);
