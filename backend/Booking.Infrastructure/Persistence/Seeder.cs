using Booking.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Booking.Infrastructure.Persistence;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        var section = config.GetSection("App:Users").Get<List<SeedUser>>() ?? [];
        foreach (var seed in section)
        {
            if (await db.Users.AnyAsync(u => u.Email == seed.Email)) continue;
            db.Users.Add(new User
            {
                Name = seed.Name,
                Email = seed.Email,
                PhoneE164 = seed.Phone,
                Role = Enum.TryParse<UserRole>(seed.Role, ignoreCase: true, out var r) ? r : UserRole.Student,
                TimeZoneId = "Europe/London"
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed record SeedUser(string Name, string Email, string? Phone, string Role);
}
