# Slice 2 Design — Google Auto-Meet + WhatsApp Reminders

Date: 2026-09-22 (rev 2 — post adversary review; rev 1 FAILed 7 kills)
Status: awaiting re-approval
Builds on: Slice 1 (live at https://lessons.giftmugweni.com)

## Decisions (from brainstorming)

| # | Question | Answer |
|---|----------|--------|
| 1 | Twilio account? | Have account, sandbox only → **graduated to production sender** (adversary K4: sandbox sessions expire every 3 days + only 3 pre-approved templates → cannot do set-and-forget rhythms) |
| 2 | Phone numbers? | Placeholders now, real numbers at deploy (via new admin endpoint — seeder skips existing rows, gap 5) |
| 3 | Google Cloud? | Manual project + OAuth Web client; **Pulumi enables Calendar API only** (adversary K1: no IaC resource exists for consent-screen web clients; ephemeral state would 409 project re-creation) |
| 4 | Scope? | Google + WhatsApp together, one release |

```
                    ┌──────────────────────────────┐
                    │ Google Cloud (MANUAL project) │
                    │  + Calendar API (via Pulumi,  │
                    │    idempotent enablement)     │
                    │  + OAuth Web client (manual)  │
                    └──────────────┬───────────────┘
                                   │ OAuth connect (admin, once)
                    ┌──────────────▼───────────────┐
                    │  App (lessons.giftmugweni)   │
                    │                              │
  booking ─────────►│  GoogleCalendarProvider      │──► Meet link + event
  created           │  : IMeetLinkProvider         │    20:30 Harare, 2h
                    │  returns (link, eventId)     │
                    │                              │
  booking/cancel/   │  ReminderService (timers)    │──► Twilio PRODUCTION
  reschedule        │  Mon 09:00 / 08:00 / 20:00   │    sender, approved
  events            │  Africa/Harare               │    templates
                    └──────────────────────────────┘
```

## Section 1 — Google: manual project, Pulumi enables API, consent in console

- **Manual one-time:** create GCP project (No organization, or state org/folder + billing if present), create **OAuth Web client** manually (redirect `https://lessons.giftmugweni.com/api/auth/google/callback`), store `GOOGLE_CLIENT_ID/SECRET` as secrets. No Terraform/Pulumi resource exists for consent-screen web clients.
- **Pulumi (CI-run, `Pulumi.Gcp` added):** enables `calendar-json.googleapis.com` only (re-apply is a no-op, safe on ephemeral state). Auth via service-account key or WIF OIDC (`GOOGLE_CREDENTIALS`), never personal ADC JSON.
- **Manual console checklist (~10 min):** consent screen External → app name + live /privacy + /terms URLs (shipped in Slice 1, must be hosted on the verified domain, verifying account must be project Owner/Editor) → test user (self) → Search Console domain check → Publish to Production.
- **After Publish: revoke/delete any Testing-era `GoogleTokens` row and re-run consent** — tokens minted in Testing keep their 7-day clock.
- **Connect:** /admin "Connect Google" → sign in as giftmugweni@gmail.com. Meetings on the teacher's calendar; teacher owns every Meet.
- **Token:** refresh token encrypted (AES-GCM, env key) in `GoogleTokens(UserId FK, RefreshTokenEncrypted, AccessToken, ExpiryUtc, Scope)`. Keep-alive timer refreshes at 50% TTL (covers idle weeks; 6-month-unused, user-revoke, password-change, and 100-token-cap expiries still apply → `invalid_grant` → reconnect banner). Unverified-app warning + 100-user cap accepted (1-user app).

## Section 2 — Meet provider + never-break fallback

| Piece | Design |
|---|---|
| Provider | `GoogleCalendarProvider : IMeetLinkProvider` where the interface returns `(string MeetLink, string? GoogleEventId)` (adversary K5: string-only return cannot persist the event id) |
| DI switch | `if config["App:Meet:Provider"]=="google" → GoogleCalendarProvider else FixedLinkMeetProvider` (aligns with `App:` config conventions; `Meet__Provider` env; compose + Pulumi `optionalKeys` updated) |
| Create | `events.insert?conferenceDataVersion=1` + `conferenceData.createRequest` (unique `requestId` per booking) → Meet + 20:30 Harare 2h event, one call; event ID + URL on Slot row |
| Cancel | `events.delete` the stored `GoogleEventId`; 404 (teacher deleted it manually) → treat as already-cancelled, continue |
| Reschedule | `delete` old event + `insert` new (new Meet link; avoids stale joins; unique `requestId`) |
| Failure | **Provider wraps all Calendar calls in try/catch → returns fixed link + sets `GoogleTokens.NeedsReconnect`**; booking handler never sees the exception (booking never fails) |
| Scope | `https://www.googleapis.com/auth/calendar.events` (full URI; sensitive scope — which is why Production publish is load-bearing) |
| Existing bookings | Untouched; only new/changed bookings use Google once connected |

## Section 3 — WhatsApp rhythms (production sender)

| Rhythm | Trigger | Content (submitted as approved templates) |
|---|---|---|
| Confirmation | booking created/cancelled/rescheduled | lesson day + your-local time + Meet (or fixed) link |
| Monday summary | Mon 09:00 Harare | week's lessons, Europe/London times |
| Morning nudge | lesson day 08:00 Harare | tonight 20:30 Harare / your-local + link |
| Evening nudge | lesson day 20:00 Harare (T-30) | "starting in 30 min" + join link |
| Login codes | Email stays primary | WhatsApp delivery only if email bounces |

- Production WhatsApp sender (pay-as-you-go ~$0.005/msg); the 4 wordings submitted as Twilio templates pre-launch. Sandbox remains for dev testing only.
- `ReminderService`: timer `BackgroundService` (BackupWorker pattern); next-fire computed in Africa/Harare via `TZConvert` (Harare = UTC+2 year-round); per-student text rendered via `LessonTime.StartUtc` + `TZConvert` to Europe/London (BST-safe, reuses tested helpers).
- **Boot catch-up (adversary K6):** on start, query Slots for lessons with `now ∈ [fireTime, fireTime + 15min grace)` and no matching `ReminderLog(to/date/template)` row → send immediately; else skip + log. `ReminderLog(to/date/template/result/TwilioSid)` is the idempotency key. Single-instance timers only (no scale-out).
- **Per-recipient isolation (adversary K7):** each send wrapped in its own try/catch → failure logged to `ReminderLog(result=failed)`, loop continues. One bad number never halts rhythms.
- **Phones:** placeholder `+00…` seeded rows replaced via new `POST /api/admin/users/phone` (seeder skips existing rows, so env re-seed cannot update them). Twilio send errors (incl. sandbox `63015`) → logged warning, never crash.
- **Rate:** Twilio throttling respected with per-send spacing + backoff on 429; Calendar `events.insert` quota likewise (retry with backoff, still inside provider try/catch).

## Section 4 — Data, errors, tests

| Area | Design |
|---|---|
| Tables | `GoogleTokens(UserId FK, RefreshTokenEncrypted, AccessToken, ExpiryUtc, Scope, NeedsReconnect)`; `ReminderLog(To, Date, Template, Result, TwilioSid)`. `Users.PhoneE164` exists |
| Errors | Google down → fixed link + banner, booking never fails. Twilio error → logged, rhythms continue. Scheduler crash → clean restart + catch-up window |
| Tests | xUnit: provider vs recorded Calendar cassette (no live Google in CI; `conferenceDataVersion=1` asserted on the recorded request), next-fire math incl. BST switch, fallback paths, catch-up logic. Playwright: Connect button, Meet link on booking, /api wiring |
| Config | `App:Meet:Provider`, `Twilio:*`, `Google:*` secrets, `Reminder:*` rhythm toggles (`Meet__Provider` style env mapping) |
| Deploy | Same pipeline (PR → CI → GHCR → Pulumi); Pulumi also enables the Calendar API; phones + Twilio creds as secrets |

## Explicit non-goals

- No Hangfire. No Meet `spaces.create`. No token in env vars. No sandbox rhythms. No scale-out schedulers.
