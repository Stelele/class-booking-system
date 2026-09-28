# Push notifications setup (one-time, human)

Replaces the old Twilio WhatsApp sender. Web Push costs nothing per message;
Reminders fall back to email when a device cannot be reached.

## 1. Generate the VAPID key pair

Once, on a machine with the .NET SDK. `Infrastructure.Notifications.VapidKeys`
produces the exact shape RFC 8292 requires (65-byte uncompressed public point,
32-byte private scalar, both base64url):

```bash
cd backend
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~VapidKeyFormatTests" -v n
```

Or add a temporary fact to `backend/Tests/Domain/VapidKeyFormatTests.cs`:

```csharp
var keys = VapidKeys.Generate();
Console.WriteLine($"public:  {keys.PublicKey}");
Console.WriteLine($"private: {keys.PrivateKey}");
```

Do this **before** any real users subscribe: rotating the pair later silently
breaks every registered device.

## 2. Store the keys as secrets

```bash
gh secret set PUSH_VAPID_PUBLIC_KEY   # safe to expose; the browser needs it
gh secret set PUSH_VAPID_PRIVATE_KEY  # server secret
gh variable set PUSH_VAPID_SUBJECT --body "mailto:lessons@giftmugweni.com"
```

> **Do not rotate these casually.** Every existing browser subscription is bound
> to the public key. Changing the pair silently breaks every registered device —
> each user must re-grant permission.

## 3. Verify

1. Deploy (or run locally with `Push__VapidPublicKey` / `Push__VapidPrivateKey`
   exported — `dotnet run --project backend/Host` reads them from the
   environment via the normal `__` config convention).
2. `GET /api/notifications/push-key` should return `{"enabled":true,...}` when
   logged in.
3. Open **My Lessons** → **Turn on reminders** → grant permission.
4. Book a slot. `ReminderLog` should show `Channel = push`.

### Running locally

`dotnet run` reads the config-style names, **not** the compose host names:

```bash
export Push__VapidPublicKey="..."      # NOT PUSH_VAPID_PUBLIC_KEY
export Push__VapidPrivateKey="..."
export Push__VapidSubject="mailto:lessons@giftmugweni.com"
dotnet run --project backend/Host
```

`PUSH_VAPID_*` is only the host-side name in `docker-compose.yml`; compose maps
it to `Push__*` inside the container. Using the wrong prefix fails silently —
`/api/notifications/push-key` returns `{"enabled":false}`.

## 4. Platform notes

| Platform | Requirement |
|---|---|
| Chrome / Edge / Android | Works immediately. No install needed. |
| Firefox | Works immediately. No install needed. |
| **iOS / iPadOS** | **Must be added to the Home Screen** and launched from its icon. `pushManager.subscribe` does not exist otherwise. The app shows "Add to Home Screen" instructions until it is running standalone. |
| Safari (macOS) | Works as an installed web app. |
| Chrome / Edge incognito | **No.** Browsers refuse the Push API in private windows. The UI detects this and explains it. |

Safari revokes the notification permission if the service worker receives a push
and does not immediately show a visible notification — `frontend/src/sw.ts`
calls `showNotification` directly for this reason. Do not "optimise" it into a
deferred/fetched notification.

### Why iOS gets both push and email

Web Push only ever confirms that the **push service accepted** the message — it
is never a receipt that the device displayed it. There is no per-message
delivery status for browser push, and no `wamid`-style id to correlate a
webhook against. So on iOS we cannot prove a notification was actually shown,
and a silently dropped lesson reminder is precisely the failure the email
fallback exists to prevent.

So: `PushSubscription.IsIos` is set by the browser at registration, and
`PushOrEmailNotifier` sends the email copy **in addition to** push whenever an
iOS device is in the recipient's subscriptions. Every other platform gets push
only, and email on API failure. Email is free, so the trade is worth it.

## 5. Not yet verified on real hardware

Everything below was proven in tests, on a real browser, and against the live
production-mode backend — but **not** on a physical iPhone:

| Verified | How |
|---|---|
| VAPID key shape accepted by the push library | `VapidKeyFormatTests` constructs `VapidAuthentication` from generated keys |
| Real VAPID signing + RFC 8291 aes128gcm encryption | `PushNotifierTests` drives the real library through a stub handler and asserts the `vapid` auth header and a non-empty encrypted body |
| Dead-endpoint pruning (404/410) | same |
| Email fallback when push does not deliver | same |
| base64url → 65-byte `applicationServerKey` | decoded in a real Chromium against the live endpoint (87 chars → 65 bytes) |
| Service worker registers and controls the page | real production build in a real browser |
| `/api/notifications/push-key` serves the key | live backend, `enabled: true` |
| Migrations + `ReminderLog.UserId` backfill | real SQLite DB seeded with legacy rows |
| Scheduler idempotency key | `ReminderServiceTests` |

Still needs a real device:

| Unverified | Why |
|---|---|
| `pushManager.subscribe()` completing | Chrome's push-service registration hangs in a containerised/CI environment |
| An actual push being delivered and displayed | needs the above |
| iOS Home Screen install → subscribe → display | needs a physical iPhone/iPad |

The iOS double-send above exists **because** that last row is unverified: if
iOS silently drops a notification, the student still gets the email.

## 6. When a device stops receiving

- The push service answers `404`/`410` for a dead endpoint; the backend prunes
  the row automatically on the next send.
- `ReminderLog.Channel` shows which path delivered: `push`, `email` or `log`.
  `log` means neither channel is configured — check the secrets above.
