using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        // Bootstrap only. Once any user exists the database is the source of
        // truth, because the admin can edit names and emails from the UI: a
        // per-email "skip if present" rule would re-add the original config
        // row as a duplicate ghost user on the next deploy.
        if (await db.Users.AnyAsync()) return;

        var seeds = config.GetSection("App:Users").Get<List<SeedUser>>() ?? [];
        foreach (var seed in seeds)
        {
            db.Users.Add(new User
            {
                Name = seed.Name,
                Email = seed.Email.Trim().ToLowerInvariant(),
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
