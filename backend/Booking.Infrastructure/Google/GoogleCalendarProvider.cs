using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Booking.Infrastructure.Meet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeZoneConverter;

namespace Booking.Infrastructure.Google;

/// Creates a Calendar event with a Meet link per lesson date. Never throws:
/// any Google failure falls back to the fixed link (and flags reconnect on 401/403).
public sealed class GoogleCalendarProvider(
    IHttpClientFactory httpFactory,
    IGoogleTokenStore tokens,
    GoogleOAuthClient oauth,
    IOptions<GoogleOAuthSettings> opts,
    GoogleTokenCrypto crypto,
    ILogger<GoogleCalendarProvider> log,
    FixedLinkMeetProvider fallback) : IMeetLinkProvider, IMeetEventSync
{
    public async Task<MeetLinkResult> GetOrCreateLinkAsync(DateOnly date, CancellationToken ct = default)
    {
        try
        {
            var token = await tokens.GetAsync(ct);
            if (token is null || token.NeedsReconnect || string.IsNullOrEmpty(token.AccessToken))
                return await fallback.GetOrCreateLinkAsync(date, ct);

            if (token.ExpiryUtc <= DateTime.UtcNow.AddMinutes(5))
            {
                token = await TryRefreshAsync(token, ct);
                if (token is null)
                    return await fallback.GetOrCreateLinkAsync(date, ct);
            }

            var http = httpFactory.CreateClient("Google");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

            var tz = TZConvert.GetTimeZoneInfo(LessonTime.ZoneId);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(20, 30)), DateTimeKind.Unspecified), tz);
            var endUtc = startUtc.AddHours(LessonTime.DurationHours);
            // Send wall-clock times with the zone so the stored event reads 20:30 Harare time.
            var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
            var endLocal = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);
            var body = new
            {
                summary = "Programming lesson",
                description = "Evening programming lesson — booked via the class booking site.",
                start = new { dateTime = startLocal.ToString("s"), timeZone = LessonTime.ZoneId },
                end = new { dateTime = endLocal.ToString("s"), timeZone = LessonTime.ZoneId },
                conferenceData = new
                {
                    createRequest = new
                    {
                        requestId = Guid.NewGuid().ToString("N"),
                        conferenceSolutionKey = new { type = "hangoutsMeet" },
                    },
                },
            };
            using var res = await http.PostAsJsonAsync(
                "calendars/primary/events?conferenceDataVersion=1&sendUpdates=all", body, ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonObject>(ct);
            var meet = json?["conferenceData"]?["entryPoints"]?.AsArray()
                .FirstOrDefault(e => e?["entryPointType"]?.GetValue<string>() == "video")
                ?["uri"]?.GetValue<string>();
            var id = json?["id"]?.GetValue<string>();
            if (meet is null || id is null)
                throw new InvalidOperationException("Meet link missing in response.");
            return new MeetLinkResult(meet, id);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // Never break a booking: fall back + flag reconnect on auth failures.
            log.LogWarning(ex, "Google Meet creation failed; using fixed link.");
            if (ex is HttpRequestException hre
                && hre.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                await tokens.FlagReconnectAsync(ct);
            return await fallback.GetOrCreateLinkAsync(date, ct);
        }
    }

    public async Task DeleteEventAsync(string googleEventId, CancellationToken ct = default)
    {
        try
        {
            var token = await tokens.GetAsync(ct);
            if (token is null || token.NeedsReconnect || string.IsNullOrEmpty(token.AccessToken))
                return;
            var http = httpFactory.CreateClient("Google");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var res = await http.DeleteAsync(
                $"calendars/primary/events/{Uri.EscapeDataString(googleEventId)}?sendUpdates=all", ct);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return; // already gone (e.g. deleted in Calendar UI)
            res.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google event delete failed for {GoogleEventId}; continuing.", googleEventId);
        }
    }

    /// Refreshes an expiring token. Returns null (after flagging reconnect + logging)
    /// when the refresh fails — the caller falls back to the fixed link.
    /// A null RefreshToken in the refresh response keeps the old encrypted refresh token.
    private async Task<GoogleTokenData?> TryRefreshAsync(GoogleTokenData token, CancellationToken ct)
    {
        try
        {
            var o = opts.Value;
            var refreshed = await oauth.RefreshAsync(
                crypto.Decrypt(token.RefreshTokenEncrypted), o.ClientId, o.ClientSecret, ct);
            var updated = token with
            {
                AccessToken = refreshed.AccessToken,
                ExpiryUtc = refreshed.ExpiryUtc,
                RefreshTokenEncrypted = refreshed.RefreshToken is null
                    ? token.RefreshTokenEncrypted
                    : crypto.Encrypt(refreshed.RefreshToken),
            };
            await tokens.SaveAsync(updated, ct);
            return updated;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Google token refresh failed; flagging reconnect.");
            await tokens.FlagReconnectAsync(ct);
            return null;
        }
    }
}
