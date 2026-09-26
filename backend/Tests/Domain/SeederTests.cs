using Domain.Users;
using Infrastructure.Persistence;using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Domain;

// The seeder is bootstrap-only: it must never resurrect a config row after the
// admin has edited that user's name/email in the UI, or a renamed user gets a
// duplicate placeholder twin back on the next deploy.
public class SeederTests
{
    private static async Task<(AppDbContext Db, SqliteConnection Conn)> NewDbAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        return (db, conn);
    }

    private static IConfiguration Config(params (string Name, string Email, string Role)[] users)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < users.Length; i++)
        {
            values[$"App:Users:{i}:Name"] = users[i].Name;
            values[$"App:Users:{i}:Email"] = users[i].Email;
            values[$"App:Users:{i}:Role"] = users[i].Role;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public async Task Empty_database_is_seeded_from_config()
    {
        var (db, conn) = await NewDbAsync();
        await using var _ = conn;
        var config = Config(("Gift Mugweni", "teacher@example.com", "Admin"),
                            ("Alice Moyo", "alice@example.com", "Student"));

        await Seeder.SeedAsync(db, config);

        Assert.Equal(2, await db.Users.CountAsync());
        var alice = await db.Users.SingleAsync(u => u.Email == "alice@example.com");
        Assert.Equal("Alice Moyo", alice.Name);
        Assert.Equal(UserRole.Student, alice.Role);
    }

    [Fact]
    public async Task Populated_database_keeps_admin_renames()
    {
        var (db, conn) = await NewDbAsync();
        await using var _ = conn;
        var config = Config(("Gift Mugweni", "gift@example.com", "Admin"));

        await Seeder.SeedAsync(db, config);
        var teacher = await db.Users.SingleAsync();
        teacher.Name = "Gift M.";
        await db.SaveChangesAsync();

        // next deploy runs the same config again — the rename must survive
        await Seeder.SeedAsync(db, config);

        Assert.Equal("Gift M.", (await db.Users.SingleAsync()).Name);
    }

    [Fact]
    public async Task Populated_database_is_not_duplicated_when_config_adds_a_user()
    {
        var (db, conn) = await NewDbAsync();
        await using var _ = conn;

        await Seeder.SeedAsync(db, Config(("Gift Mugweni", "gift@example.com", "Admin")));
        // a later deploy's config also lists a newcomer
        await Seeder.SeedAsync(db, Config(("Gift Mugweni", "gift@example.com", "Admin"),
                                          ("Brand New", "newcomer@example.com", "Student")));

        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Populated_database_is_not_duplicated_when_config_keeps_a_stale_placeholder()
    {
        // the exact production trap: the admin edits an email, so the old
        // config row no longer matches and would be re-added as a ghost twin
        var (db, conn) = await NewDbAsync();
        await using var _ = conn;
        var config = Config(("Student A", "studenta@example.com", "Student"));
        await Seeder.SeedAsync(db, config);

        var student = await db.Users.SingleAsync();
        student.Email = "alice@example.com";
        student.Name = "Alice Moyo";
        await db.SaveChangesAsync();

        await Seeder.SeedAsync(db, config);

        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal("alice@example.com", (await db.Users.SingleAsync()).Email);
    }
}
