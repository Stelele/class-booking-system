using Booking.Application.Abstractions;
using Booking.Application.DTOs;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Auth;

public sealed class VerifyCodeCommandHandler(IAppDbContext db)
    : ICommandHandler<VerifyCodeCommand, UserDto>
{
    public async Task<UserDto> Handle(VerifyCodeCommand c, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email.Trim().ToLower(), ct)
                   ?? throw new UnauthorizedAccessException("Invalid code.");

        var hash = CodeGenerator.Hash(c.Code.Trim());
        var match = await db.AuthCodes
            .Where(a => a.UserId == user.Id && a.CodeHash == hash && a.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(ct);
        if (match.Count == 0) throw new UnauthorizedAccessException("Invalid code.");

        db.AuthCodes.RemoveRange(match); // single-use
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Name, user.Email, user.Role.ToString());
    }
}
