using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Application.Users;

public sealed partial class UpdateUserCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<UpdateUserCommand, AdminUserDto>
{
    // matches the Users.Name column (AppDbContext.OnModelCreating)
    private const int MaxNameLength = 100;

    public async Task<AdminUserDto> Handle(UpdateUserCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == c.Id, ct)
                     ?? throw new UserNotFoundException("No such user.");

        var name = c.Name.Trim();
        if (name.Length is 0 or > MaxNameLength)
            throw new UserException($"Name must be 1-{MaxNameLength} characters.");

        // login lookups normalise to lower case (RequestCodeCommandHandler), so a
        // mixed-case stored email would lock the user out of their own account
        var email = c.Email.Trim().ToLowerInvariant();
        if (!EmailPattern().IsMatch(email)) throw new UserException("Enter a valid email address.");
        if (await db.Users.AnyAsync(u => u.Id != c.Id && u.Email == email, ct))
            throw new UserException("That email is already used by another user.");

        var phone = c.Phone?.Trim();
        if (string.IsNullOrEmpty(phone)) phone = null;
        else if (!PhonePattern().IsMatch(phone))
            throw new UserException("Phone must be E.164, e.g. +447700900123.");

        target.Name = name;
        target.Email = email;
        target.PhoneE164 = phone;
        await db.SaveChangesAsync(ct);

        return new AdminUserDto(target.Id, target.Name, target.Email, target.PhoneE164, target.Role.ToString());
    }

    // deliberately loose: one @ with non-empty, dot-bearing sides
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // E.164 is ASCII-only; \d would also accept Unicode decimal digits
    [GeneratedRegex(@"^\+[0-9]{7,15}$")]
    private static partial Regex PhonePattern();
}
