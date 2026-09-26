using Application.Abstractions;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Application.Auth;

public sealed record GetMeQuery : IQuery<UserDto?>;

public sealed class GetMeQueryHandler(IAppDbContext db, ICurrentUser user)
    : IQueryHandler<GetMeQuery, UserDto?>
{
    // Reads the row rather than ICurrentUser.Name: the session cookie carries
    // the name as a claim for 30 days, so trusting it would keep showing a
    // pre-rename name in the header until the user logged in again.
    public async Task<UserDto?> Handle(GetMeQuery q, CancellationToken ct)
    {
        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.UserId, ct);
        return me is null ? null : new UserDto(me.Id, me.Name, me.Email, me.Role.ToString());
    }
}
