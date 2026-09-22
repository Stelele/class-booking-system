# Slice 2 Design — Google Auto-Meet + WhatsApp Reminders

Date: 2026-09-22
Status: design approved (all 4 sections)
Builds on: Slice 1 (live at https://lessons.giftmugweni.com)

## Decisions (from brainstorming)

| # | Question | Answer |
|---|----------|--------|
| 1 | Twilio account? | Have account, **sandbox only** |
| 2 | Phone numbers? | Placeholders now, real numbers at deploy |
| 3 | Google Cloud? | Pulumi creates project + APIs + OAuth client; consent screen is manual (Google blocks IaC there) |
| 4 | Scope? | Google + WhatsApp together, one release |

```
                    ┌──────────────────────────────┐
                    │ Google Cloud (Pulumi-managed) │
                    │  project class-booking-prod   │
                    │  + Calendar API + OAuth client│
                    └──────────────┬───────────────┘
                                   │ OAuth connect (admin, once)
                    ┌──────────────▼───────────────┐
                    │  App (lessons.giftmugweni)   │
                    │                              │
  booking ─────────►│  GoogleCalendarProvider      │──► Meet link + event
  created           │  : IMeetLinkProvider         │    20:30 Harare, 2h
                    │  (Meet:Provider=google)      │
                    │                              │
  booking/cancel/   │  ReminderService (timers)    │──► Twilio sandbox
  reschedule        │  Mon 09:00 / 08:00 / 20:00   │    per-student TZ
  events            │  Africa/Harare               │    Europe/London text
                    └──────────────────────────────┘
```

## Section 1 — Google in Pulumi, consent in console

- **Pulumi (new stack file, CI-run):** project `class-booking-prod`, Calendar API enabled, OAuth client (Web, redirect `https://lessons.giftmugweni.com/api/auth/google/callback`). Needs one GCP credential in CI (personal-account ADC JSON, pasted as a secret once).
- **Manual console checklist (one time, ~10 min):** consent screen External → app name + live /privacy + /terms URLs (already shipped in Slice 1) → test user (self) → Search Console domain check → Publish to Production.
- **Connect:** /admin "Connect Google" → sign in as giftmugweni@gmail.com. Meetings live on the teacher's calendar; teacher owns every Meet.
- **Token:** refresh token encrypted (env key) in `GoogleTokens` table; silent refresh before expiry; `invalid_grant` → reconnect banner.

## Section 2 — Meet provider + never-break fallback

| Piece | Design |
|---|---|
| Provider | `GoogleCalendarProvider : IMeetLinkProvider` — config-only swap (`Meet:Provider=google`) |
| Create | `events.insert?conferenceDataVersion=1` + `conferenceData.createRequest` → Meet + 20:30 Harare 2h event, one call; event ID + URL on Slot row |
| Cancel/reschedule | Same Google event updated/cancelled (Google notifies guests); `GoogleEventId` kept |
| No token/expired | Silent fixed-link fallback (Slice 1) + admin "Reconnect Google" banner |
| Scope | `calendar.events` only |
| Existing bookings | Untouched; only new/changed bookings use Google once connected |

## Section 3 — WhatsApp rhythms + sandbox reality

| Rhythm | Trigger | Content |
|---|---|---|
| Confirmation | booking created/cancelled/rescheduled | day + your-local time + Meet (or fixed) link |
| Monday summary | Mon 09:00 Harare | week's lessons, Europe/London times |
| Morning nudge | lesson day 08:00 Harare | tonight 20:30 Harare / your-local + link |
| Evening nudge | lesson day 20:00 Harare (T-30) | "starting in 30 min" + join link |
| Login codes | Email stays primary | WhatsApp delivery available only if email bounces |

- `ReminderService`: timer `BackgroundService` (BackupWorker pattern); next-fire computed in Africa/Harare; recomputed at boot.
- `ITwilioSender`: sandbox number now, production sender later = config swap.
- Phones: placeholder `+00…` seeded rows, real numbers pre-go-live, no code change.
- Sandbox join: teacher sends both students the one-time "text JOIN to +1…" message out-of-band. Unsent/unjoined → logged warning, never crash. Every send logged.

## Section 4 — Data, errors, tests

| Area | Design |
|---|---|
| Tables | `GoogleTokens` (encrypted refresh token, expiry, scope); `ReminderLog` (to/date/template/result). `Users.PhoneE164` exists |
| Errors | Google down → fixed link + banner (booking never fails). Twilio error → logged, rhythms continue. Scheduler crash → clean restart, recompute |
| Tests | xUnit: provider vs recorded Calendar response (no live Google in CI), next-fire math incl. BST switch, fallbacks. Playwright: Connect button, Meet link on booking, /api wiring |
| Config | `Meet:Provider`, `Twilio:*`, `Google:*` secrets, `Reminder:*` rhythm toggles |
| Deploy | Same pipeline (PR → CI → GHCR → Pulumi); Pulumi also makes the GCP project; phones + Twilio creds as secrets |

## Explicit non-goals

- No Hangfire (timers suffice for 3 users).
- No Meet API `spaces.create` (Calendar `conferenceData` does it in one call).
- No token in env vars (DB table survives restarts).
- No WhatsApp template pre-approval work (sandbox first; production sender later).
