using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
            var token = await EnsureFreshTokenAsync(ct);
            if (token is null)
                return await fallback.GetOrCreateLinkAsync(date, ct);

            var http = httpFactory.CreateClient("Google");

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
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "calendars/primary/events?conferenceDataVersion=1&sendUpdates=all")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var res = await http.SendAsync(request, ct);
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
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or CryptographicException or TaskCanceledException)
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
            var token = await EnsureFreshTokenAsync(ct);
            if (token is null)
                return;
            var http = httpFactory.CreateClient("Google");
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"calendars/primary/events/{Uri.EscapeDataString(googleEventId)}?sendUpdates=all");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var res = await http.SendAsync(request, ct);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return; // already gone (e.g. deleted in Calendar UI)
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                await tokens.FlagReconnectAsync(ct);
                return; // booking already succeeded; never throw
            }
            if (!res.IsSuccessStatusCode)
                log.LogWarning("Google event delete returned {Status} for {GoogleEventId}; continuing.",
                    res.StatusCode, googleEventId);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google event delete failed for {GoogleEventId}; continuing.", googleEventId);
        }
    }

    /// Shared ensure-fresh-token helper for the insert and delete paths: returns
    /// null when there is no usable token (missing, NeedsReconnect, empty, or
    /// refresh failed) so callers skip/fall back; otherwise a fresh access token.
    private async Task<GoogleTokenData?> EnsureFreshTokenAsync(CancellationToken ct)
    {
        var token = await tokens.GetAsync(ct);
        if (token is null || token.NeedsReconnect || string.IsNullOrEmpty(token.AccessToken))
            return null;

        if (token.ExpiryUtc <= DateTime.UtcNow.AddMinutes(5))
            token = await TryRefreshAsync(token, ct);
        return token;
    }

    /// Refreshes an expiring token. Returns null (after flagging reconnect + logging)
    /// when the refresh fails — the caller falls back to the fixed link.
    /// A null RefreshToken in the refresh response keeps the old encrypted refresh token.
    private async Task<GoogleTokenData?> TryRefreshAsync(GoogleTokenData token, CancellationToken ct)
    {
        string refreshToken;
        try
        {
            refreshToken = crypto.Decrypt(token.RefreshTokenEncrypted);
        }
        catch (CryptographicException ex)
        {
            await tokens.FlagReconnectAsync(ct);
            throw new InvalidOperationException("Stored Google token is unreadable; reconnect required.", ex);
        }

        try
        {
            var o = opts.Value;
            var refreshed = await oauth.RefreshAsync(
                refreshToken, o.ClientId, o.ClientSecret, ct);
            var updated = token with
            {
                AccessToken = refreshed.AccessToken,
                ExpiryUtc = refreshed.ExpiryUtc,
                RefreshTokenEncrypted = refreshed.RefreshToken is null
                    ? token.RefreshTokenEncrypted
                    : crypto.Encrypt(refreshed.RefreshToken),
            };
            // Refresh runs on an existing row, so its UserId is preserved by the
            // store's update path; Guid.Empty is only a fallback lookup key for the
            // duplicate-race retry. Fresh connects (Task 6) supply the real user id.
            await tokens.SaveAsync(updated, Guid.Empty, ct);
            return updated;
        }
        catch (HttpRequestException ex) when (IsAuthLoss(ex))
        {
            log.LogWarning(ex, "Google token refresh rejected; flagging reconnect.");
            await tokens.FlagReconnectAsync(ct);
            return null;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Transient Google token refresh failure; using fixed link without flagging reconnect.");
            return null;
        }
    }

    /// Auth loss = 401/403, or 400 carrying invalid_grant. Transient
    /// (429/5xx/no status) must NOT flag reconnect.
    private static bool IsAuthLoss(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        || (ex.StatusCode is HttpStatusCode.BadRequest
            && ex.Message.Contains("invalid_grant", StringComparison.Ordinal));
}
