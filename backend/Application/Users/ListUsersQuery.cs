using Application.Abstractions;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Application.Users;

public sealed record ListUsersQuery : IQuery<List<AdminUserDto>>;

public sealed class ListUsersQueryHandler(IAppDbContext db, ICurrentUser user)
    : IQueryHandler<ListUsersQuery, List<AdminUserDto>>
{
    public async Task<List<AdminUserDto>> Handle(ListUsersQuery q, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");

        // Role.ToString() is not translatable in the projection — map in memory
        var rows = await db.Users.AsNoTracking()
            .OrderBy(u => u.Role).ThenBy(u => u.Name)
            .Select(u => new { u.Id, u.Name, u.Email, u.PhoneE164, u.Role })
            .ToListAsync(ct);

        return rows.Select(u => new AdminUserDto(u.Id, u.Name, u.Email, u.PhoneE164, u.Role.ToString())).ToList();
    }
}
