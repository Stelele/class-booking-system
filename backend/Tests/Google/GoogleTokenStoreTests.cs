using Application.Abstractions;
using Domain.Auth;
using Infrastructure.Google;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tests.Google;

public sealed class GoogleTokenStoreTests
{
    [Fact]
    public async Task Save_persists_owned_calendar_scope()
    {
        await using var db = NewDb();
        var store = new EfGoogleTokenStore(db);

        await store.SaveAsync(
            new GoogleTokenData("encrypted", "access", DateTime.UtcNow.AddHours(1), false),
            Guid.NewGuid(),
            CancellationToken.None);

        var row = await db.GoogleTokens.SingleAsync();
        Assert.Equal("https://www.googleapis.com/auth/calendar.events.owned", row.Scope);
    }

    [Fact]
    public async Task Get_marks_existing_broad_token_as_needing_reconnect()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.GoogleTokens.Add(new GoogleToken
        {
            UserId = userId,
            RefreshTokenEncrypted = "encrypted",
            AccessToken = "access",
            ExpiryUtc = DateTime.UtcNow.AddHours(1),
            Scope = "https://www.googleapis.com/auth/calendar.events",
        });
        await db.SaveChangesAsync();
        var store = new EfGoogleTokenStore(db);

        var token = await store.GetAsync(CancellationToken.None);

        Assert.NotNull(token);
        Assert.True(token.NeedsReconnect);
    }

    [Fact]
    public async Task Save_updates_existing_token_scope()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        db.GoogleTokens.Add(new GoogleToken
        {
            UserId = userId,
            RefreshTokenEncrypted = "old-encrypted",
            AccessToken = "old-access",
            ExpiryUtc = DateTime.UtcNow,
            Scope = "https://www.googleapis.com/auth/calendar.events",
        });
        await db.SaveChangesAsync();
        var store = new EfGoogleTokenStore(db);

        await store.SaveAsync(
            new GoogleTokenData("new-encrypted", "new-access", DateTime.UtcNow.AddHours(1), false),
            userId,
            CancellationToken.None);

        Assert.Equal(
            "https://www.googleapis.com/auth/calendar.events.owned",
            (await db.GoogleTokens.SingleAsync()).Scope);
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
