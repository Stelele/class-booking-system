using Booking.Application.Abstractions;
using Booking.Application.Auth;
using Booking.Domain.Users;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Booking.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public static string LastCode { get; private set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // fresh SQLite file per test-host run (pooled connections open per context)
        var path = Path.Combine(Path.GetTempPath(), $"tests-{Guid.NewGuid():N}.db");
        builder.UseSetting("App:DbPath", path);
        builder.ConfigureTestServices(services =>
        {
            // fake code sender records the code for tests
            services.RemoveAll<ICodeSender>();
            services.AddSingleton<ICodeSender>(new FakeSender());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        // fixture users (config-based seeding is exercised by E2E which runs the real Host)
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new User { Name = "Teacher", Email = "teacher@example.com", Role = UserRole.Admin, TimeZoneId = "Africa/Harare" },
                new User { Name = "Student A", Email = "studenta@example.com", TimeZoneId = "Europe/London" },
                new User { Name = "Student B", Email = "studentb@example.com", TimeZoneId = "Europe/London" });
            db.SaveChanges();
        }
        return host;
    }

    private sealed class FakeSender : ICodeSender
    {
        public Task SendAsync(string email, string name, string code, CancellationToken ct = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
