using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.Auth;

public sealed class RequestCodeCommandHandler(IAppDbContext db, ICodeSender sender)
    : ICommandHandler<RequestCodeCommand, bool>
{
    public async Task<bool> Handle(RequestCodeCommand c, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email.Trim().ToLower(), ct);
        if (user is null) return true; // do not reveal registered emails

        var code = CodeGenerator.Generate6();
        db.AuthCodes.Add(new AuthCode
        {
            UserId = user.Id,
            CodeHash = CodeGenerator.Hash(code),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync(ct);
        await sender.SendAsync(user.Email, user.Name, code, ct);
        return true;
    }
}
