using Application.Abstractions;
using Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth;

public sealed class RequestCodeCommandHandler(IAppDbContext db, ICodeSender sender)
    : ICommandHandler<RequestCodeCommand, bool>
{
    public async Task<bool> Handle(RequestCodeCommand c, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email.Trim().ToLower(), ct);
        // private 3-user app: a wrong address is a typo, not an attack —
        // silent anti-enumeration would just look like a swallowed email
        if (user is null) throw new AuthException("No account for that email — check for typos, or ask your teacher to add you.");

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
