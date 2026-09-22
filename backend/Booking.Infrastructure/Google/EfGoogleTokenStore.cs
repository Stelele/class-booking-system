using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Google;

/// EF implementation: single row for the teacher (Admin). UserId recorded for audit.
public sealed class EfGoogleTokenStore(IAppDbContext db, ICurrentUser user) : IGoogleTokenStore
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";

    public async Task<GoogleTokenData?> GetAsync(CancellationToken ct)
    {
        var row = await db.GoogleTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new GoogleTokenData(row.RefreshTokenEncrypted, row.AccessToken ?? "", row.ExpiryUtc, row.NeedsReconnect);
    }

    public async Task SaveAsync(GoogleTokenData token, CancellationToken ct)
    {
        var row = await db.GoogleTokens.OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new GoogleToken
            {
                UserId = user.UserId,
                RefreshTokenEncrypted = token.RefreshTokenEncrypted,
                AccessToken = token.AccessToken,
                ExpiryUtc = token.ExpiryUtc,
                Scope = CalendarScope,
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

        await db.SaveChangesAsync(ct);
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
