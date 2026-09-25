# Google Disconnect and Privacy Compliance Design

**Date:** 2026-09-25  
**Status:** Approved design; implementation not started

## Decision

```text
Current broad grant                    Target controlled grant
┌──────────────────────────┐            ┌────────────────────────────┐
│ calendar.events          │  deploy    │ calendar.events.owned      │
│ no in-app unlink         │ ─────────► │ revoke + local deletion   │
│ inaccurate privacy text  │            │ accurate privacy controls  │
└──────────────────────────┘            └────────────────────────────┘
```

Use the existing OAuth web client and admin page. Add a first-class disconnect flow, narrow the runtime scope to `calendar.events.owned`, and replace the privacy page with a clear Google-data disclosure. Video creation is not part of this work.

## Current State

```text
OAuth request ───────────────► calendar.events
Token row ───────────────────► encrypted in SQLite
Remote revocation on reconnect only
Admin UI ────────────────────► connect / reconnect only
Privacy page ────────────────► “Nothing else / never shared”
Verification video ──────────► out of scope
```

| Surface | Current | Target |
|---|---|---|
| OAuth authorization URL | `calendar.events` | `calendar.events.owned` |
| Stored token scope | `calendar.events` | `calendar.events.owned` on reconnect |
| Admin connected state | No unlink action | Disconnect button and confirmation modal |
| Remote grant | Cannot be revoked from the app | Revoked on disconnect |
| Active local credential | Cannot be deleted from the app | Deleted on disconnect even if revocation fails |
| Existing Calendar events | No lifecycle action | Preserved and controlled in Google Calendar |
| Privacy disclosure | Inaccurate after Google integration | Complete plain-language policy |

## Goals

```text
Goal                         Evidence
──────────────────────────────────────────────────────────────
Minimum Google access        Only calendar.events.owned is requested
Real user control            Admin can revoke and disconnect
Safe local cleanup           Active token row always deleted
No student disruption        Existing events and Meet links remain
Verification readiness       Policy matches actual data behavior
Narrow migration             Old broad grant revoked and reconnected
```

## Non-Goals

- Deleting or modifying existing Google Calendar events during disconnect.
- Automatically switching or redeploying the GitHub `MEET_PROVIDER` secret.
- Building a full settings/history center.
- Purging every historical R2 backup snapshot.
- Producing the Google verification demo video.
- Requesting profile, email, Drive, Gmail, or full Calendar access.

## Scope Transition

```text
backend/Application/Auth/BeginGoogleOAuthQueryHandler
backend/Infrastructure/Google/EfGoogleTokenStore
                 │
                 └──► GoogleOAuthScopes.CalendarEventsOwned
                         = https://www.googleapis.com/auth/calendar.events.owned
```

Create one public constant and use it in both the authorization URL and the token row’s `Scope` field. Remove every runtime occurrence of the broader `calendar.events` value.

The narrower scope supports the event insert and delete operations already used against `calendars/primary`. The application does not use it to read existing event content.

## Disconnect Architecture

```text
Admin UI
   │ DELETE /api/admin/google
   ▼
DisconnectGoogleCommandHandler
   │
   ▼
IGoogleAccountConnector.DisconnectAsync
   │
   ├── read active GoogleToken
   ├── decrypt refresh token
   ├── POST oauth2.googleapis.com/revoke
   │      non-2xx/decryption/network failure → warning only
   └── finally delete active GoogleTokens row
                         │
                         ▼
              HTTP 200 { remoteRevoked }
```

Extend `IGoogleTokenStore` with deletion and `IGoogleAccountConnector` with disconnect behavior. The infrastructure connector owns Google HTTP and crypto; the command owns orchestration; the endpoint only maps an authorized request to the command.

Remote revocation is attempted for the refresh token and the endpoint returns whether Google confirmed it. A failed or indeterminate revocation must not prevent deletion of the active local credential; the UI must then tell the user how to remove any remaining grant from Google Account settings. If local deletion fails, return an error rather than reporting success.

## Disconnect Semantics

```text
Disconnect
    │
    ├── Google API grant ─────────► revoke attempted; result reported
    ├── active SQLite token row ──► deleted
    ├── existing Calendar events ─► preserved
    ├── existing Meet links ──────► preserved
    ├── future event creation ────► fixed-link fallback
    └── reconnect ────────────────► explicitly initiated by admin
```

Google’s OAuth policy requires tokens to be revoked when the app no longer needs access and then deleted. A successful disconnect does not remove data already created in the teacher’s Google Calendar.

## Admin UI

```text
┌─ Google Calendar ───────────────────── Connected ┐
│ Create/remove owned-calendar lesson events.     │
│ Permission: calendar.events.owned               │
│                                  [Disconnect]  │
└────────────────────────────────────────────────┘
                         │ click
                         ▼
┌─ Disconnect Google Calendar? ────────────────────┐
│ Revoke API access and delete local credentials. │
│ Existing events and links remain in Google.     │
│ New bookings use the configured fallback link. │
│                         [Cancel] [Disconnect]   │
└────────────────────────────────────────────────┘
```

- Show the card and Disconnect action only for a healthy connected state.
- Keep the reconnect banner and Connect action unchanged for missing or reconnect-required tokens.
- Require explicit modal confirmation.
- Disable duplicate requests while disconnecting.
- On confirmed revocation, show a success notice, refresh status, and restore the Connect action.
- If local access was removed but Google revocation was not confirmed, show a warning with Google Account Settings removal instructions.
- On failure, keep the connected state and show a sanitized error.
- Link the privacy policy from the card or nearby helper text.

## Privacy Policy

```text
Data entered ─► account/contact + bookings
Google grant ─► encrypted token + owned event ID + Meet link
Booking flow ─► student receives invitation/cancellation
Service ops ──► DigitalOcean + Cloudflare + email/WhatsApp providers
User control ─► disconnect + Google revoke + deletion/contact request
```

The plain-language policy must disclose:

| Topic | Required disclosure |
|---|---|
| Account data | Email, optional phone/WhatsApp number, login codes, booking history |
| Google data | Teacher-authorized refresh token, event identifier, event time, Meet link |
| Purpose | Create and remove lesson events on calendars the teacher owns |
| Recipient | The booked student receives the Calendar invitation and changes/cancellations |
| Excluded use | No unrelated event reading, advertising, sale, profiling, or AI training |
| Security | HTTPS, AES-256-GCM token encryption, encryption key stored separately from the database |
| Infrastructure | DigitalOcean application host, Cloudflare R2 backups, Resend email, Twilio WhatsApp when configured, Google Calendar |
| Security logs | Hosting/security systems may process IP address, request time, route, and user agent |
| Retention | Login codes expire; booking data remains while the service operates; Google token remains until disconnect |
| Backups | Encrypted historical database snapshots may retain revoked token records; revoked tokens cannot authorize Google access |
| Disconnect | Asks Google to revoke the grant and always deletes the active local token row; explains manual Google removal if remote revocation is not confirmed |
| Existing events | Remain in the teacher’s Google Calendar after disconnect and can be managed there |
| Deletion/contact | Users can request account/data deletion by email; include `giftmugweni@gmail.com` |
| Policy date | “Last updated: 25 September 2026” |

The page must not claim data is never shared or that nothing beyond booking data is stored.

## Error Handling

```text
Remote revoke failure ─► delete local row + HTTP 200 remoteRevoked=false + UI instructions
Decrypt failure ───────► delete local row + HTTP 200 remoteRevoked=false + UI instructions
Local delete failure ──► HTTP 500 + keep truthful connected/reconnect state
No existing token ─────► idempotent HTTP 200 remoteRevoked=true
Reconnect afterward ───► request owned scope + save only after valid token
```

Logs must never contain authorization codes, access tokens, refresh tokens, encryption keys, or complete student email/phone values.

## Testing Strategy

```text
Backend
├─ authorization URL contains owned scope, not broad scope
├─ token store scope uses owned constant
├─ disconnect revokes refresh token
├─ revoke failure still deletes local token
├─ decrypt failure still deletes local token
├─ repeated disconnect is idempotent
├─ anonymous/student requests are rejected
└─ admin disconnect returns revocation result

Frontend
├─ connected state shows permission and Disconnect
├─ modal explains event preservation and fallback
├─ cancel sends no request
├─ success updates status and shows Connect
├─ unconfirmed remote revocation shows Google settings instructions
├─ local deletion failure keeps connected state
└─ privacy policy renders all required sections

Repository
├─ dotnet tests
├─ frontend unit tests
├─ frontend typecheck + production build
├─ Playwright E2E
└─ GitHub CI + deploy + production health
```

## Rollout

```text
1. Deploy owned scope + disconnect + privacy policy
                 │
2. Disconnect current broad grant ─► remote revoke + local delete
                 │
3. Reconnect from /admin ────────► grant owned scope only
                 │
4. Set MEET_PROVIDER=google ─────► manual deploy
                 │
5. Book/cancel verification ─────► event create/delete and slot release
```

The broad grant is revoked explicitly after deployment. The application must not silently reuse it under the narrower project configuration.

## Success Criteria

| Area | Pass condition |
|---|---|
| Scope | Authorization URL and stored scope use only `calendar.events.owned` |
| Remote control | Admin can disconnect; the result truthfully reports whether Google confirmed remote revocation |
| Local cleanup | Active credential row is deleted even when remote revocation fails |
| Event safety | Existing Calendar events and Meet links remain unchanged |
| UI | Confirmation is explicit; connected/disconnected states are truthful |
| Privacy | Policy matches actual storage, sharing, encryption, backup, and deletion behavior |
| Migration | Old broad grant is revoked and replaced through one explicit reconnect |
| Verification | CI, deploy, health, booking, cancellation, and slot-release checks pass |

## Assumptions

- Calendar API remains enabled in the production Google Cloud project.
- The production OAuth app remains External and published.
- The Google verification configuration already uses the owned-calendar scope.
- Existing Calendar events should survive disconnect.
- R2 backup objects are access-controlled and encrypted at rest, but no lifecycle purge is added in this work.
- The teacher is the only Google Calendar account connected to the application.
