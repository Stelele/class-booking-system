using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        var seeds = config.GetSection("App:Users").Get<List<SeedUser>>() ?? [];
        foreach (var seed in seeds)
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

    public sealed class SeedUser
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string Role { get; set; } = "Student";
    }
}
