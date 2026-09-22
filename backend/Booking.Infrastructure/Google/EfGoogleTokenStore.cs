using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Google;

/// EF implementation: single row for the teacher (Admin). UserId recorded for audit.
public sealed class EfGoogleTokenStore(IAppDbContext db) : IGoogleTokenStore
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    public async Task<GoogleTokenData?> GetAsync(CancellationToken ct)
    {
        var row = await db.GoogleTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new GoogleTokenData(row.RefreshTokenEncrypted, row.AccessToken ?? "", row.ExpiryUtc, row.NeedsReconnect);
    }

    public async Task SaveAsync(GoogleTokenData token, Guid userId, CancellationToken ct)
    {
        var row = await db.GoogleTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            if (userId == Guid.Empty) throw new InvalidOperationException("Cannot create a token row without a user id.");
            row = new GoogleToken
            {
                UserId = userId,
                RefreshTokenEncrypted = token.RefreshTokenEncrypted,
                AccessToken = token.AccessToken,
                ExpiryUtc = token.ExpiryUtc,
                Scope = CalendarScope,
                NeedsReconnect = token.NeedsReconnect,
            };
            db.GoogleTokens.Add(row);
        }
        else
        {
            row.RefreshTokenEncrypted = token.RefreshTokenEncrypted;
            row.AccessToken = token.AccessToken;
            row.ExpiryUtc = token.ExpiryUtc;
            row.NeedsReconnect = token.NeedsReconnect;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            row = await db.GoogleTokens.FirstAsync(t => t.UserId == userId, ct);
            row.RefreshTokenEncrypted = token.RefreshTokenEncrypted;
            row.AccessToken = token.AccessToken;
            row.ExpiryUtc = token.ExpiryUtc;
            row.NeedsReconnect = token.NeedsReconnect;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task FlagReconnectAsync(CancellationToken ct)
    {
        var row = await db.GoogleTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        if (row is not null)
        {
            row.NeedsReconnect = true;
            await db.SaveChangesAsync(ct);
        }
    }
}
