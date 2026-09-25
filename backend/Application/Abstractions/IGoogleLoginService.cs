namespace Application.Abstractions;

public interface IGoogleLoginService
{
    Task<GoogleLoginIdentity> AuthenticateAsync(string code, CancellationToken ct);
}
