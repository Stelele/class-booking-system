using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkPushSubscription = Lib.Net.Http.WebPush.PushSubscription;
using StoredSubscription = Domain.Reminders.PushSubscription;

namespace Infrastructure.Notifications;

/// <param name="IosDevices">How many accepted pushes went to an iOS device,
/// whose display cannot be confirmed.</param>
public sealed record SendOutcome(int Delivered, int Failed, int Pruned, int IosDevices = 0);

/// <summary>
/// Sends web push messages (RFC 8292 VAPID auth, RFC 8291 aes128gcm payload
/// encryption) to a user's registered browser endpoints. Dead endpoints —
/// the push service answers 404/410 — are pruned so they stop costing a round
/// trip on every future send.
///
/// Scoped: it reads PushSubscriptions through the request's DbContext. Only
/// the VAPID token cache is shared, via the singleton IVapidTokenCache.
/// </summary>
public sealed class WebPushSender(
    IAppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<PushOptions> options,
    IVapidTokenCache tokenCache,
    ILogger<WebPushSender> log)
{
    private readonly Lazy<VapidAuthentication?> _auth = new(() =>
    {
        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.VapidPublicKey) || string.IsNullOrEmpty(opts.VapidPrivateKey))
            return null;
        return new VapidAuthentication(opts.VapidPublicKey, opts.VapidPrivateKey)
        {
            Subject = opts.VapidSubject,
            TokenCache = tokenCache,
        };
    });

    /// <summary>False when no VAPID key pair is configured (dev / E2E / CI).</summary>
    public bool Enabled => _auth.Value is not null;

    /// <summary>Endpoint a notification click should open.</summary>
    private const string ClickUrl = "/calendar";

    public async Task<SendOutcome> SendAsync(
        Guid userId, string title, string body, NotifyUrgency urgency, CancellationToken ct)
    {
        var auth = _auth.Value;
        if (auth is null) return new SendOutcome(0, 0, 0, 0);

        var subs = await db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        if (subs.Count == 0) return new SendOutcome(0, 0, 0, 0);

        var message = new PushMessage(JsonSerializer.Serialize(new
        {
            title,
            body,
            url = ClickUrl,
            // Coalesce identical repeats: the service worker replaces the
            // existing notification rather than stacking duplicates.
            tag = Tag(title, body),
        }))
        {
            Urgency = urgency == NotifyUrgency.High
                ? PushMessageUrgency.High
                : PushMessageUrgency.Normal,
            // Lesson reminders are worthless a day later, and a short TTL lets
            // the push service drop them rather than hold them.
            TimeToLive = 60 * 60 * 6,
        };

        // One pooled client per send batch; it holds no per-message state.
        var client = new PushServiceClient(httpClientFactory.CreateClient("Push"));
        var delivered = 0;
        var failed = 0;
        var pruned = 0;
        var iosDevices = 0;

        foreach (var sub in subs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await client.RequestPushMessageDeliveryAsync(ToSdk(sub), message, auth, ct);
                delivered++;
                if (sub.IsIos) iosDevices++;
            }
            catch (PushServiceClientException ex) when (IsGone(ex.StatusCode))
            {
                // The endpoint is permanently invalid — drop the row.
                log.LogInformation("Pruning dead push endpoint for user {UserId}", userId);
                db.PushSubscriptions.Remove(sub);
                pruned++;
            }
            catch (Exception ex)
            {
                failed++;
                log.LogWarning(ex, "Push to {Endpoint} failed.", sub.Endpoint);
            }
        }

        if (pruned > 0) await db.SaveChangesAsync(ct);
        return new SendOutcome(delivered, failed, pruned, iosDevices);
    }

    private static bool IsGone(HttpStatusCode? status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private static string Tag(string title, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{title}|{body}")))[..16];

    private static SdkPushSubscription ToSdk(StoredSubscription sub)
    {
        var sdk = new SdkPushSubscription { Endpoint = sub.Endpoint };
        sdk.SetKey(PushEncryptionKeyName.P256DH, sub.P256Dh);
        sdk.SetKey(PushEncryptionKeyName.Auth, sub.Auth);
        return sdk;
    }
}
