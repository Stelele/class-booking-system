using Application.Abstractions;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth;

public sealed class CompleteGoogleLoginCommandHandler(
    IGoogleOAuthStateStore states, IGoogleLoginService login, IAppDbContext db)
    : ICommandHandler<CompleteGoogleLoginCommand, UserDto>
{
    public async Task<UserDto> Handle(CompleteGoogleLoginCommand c, CancellationToken ct)
    {
        if (!states.Consume(c.State, "login", c.Binding))
            throw new AuthException("Google login state mismatch — restart the sign-in.");
        var identity = await login.AuthenticateAsync(c.Code, ct);
        if (!identity.EmailVerified)
            throw new AuthException("Google account email is not verified.");

        var email = identity.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
                   ?? throw new AuthException("No account for that Google email — ask your teacher to add you.");
        return new UserDto(user.Id, user.Name, user.Email, user.Role.ToString());
    }
}
