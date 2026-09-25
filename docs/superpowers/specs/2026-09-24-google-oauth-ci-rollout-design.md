# Google OAuth CI Rollout Design

**Date:** 2026-09-24  
**Status:** Approved design; implementation not started

## Decision

```text
Existing GitHub Actions pipeline
          │
          ├── regression-test and fix stale slot lifecycle
          ├── merge to main and keep fixed-link mode
          ├── add four Google runtime secrets
          ├── deploy in fixed-link mode
          ├── interactive Google consent
          ├── switch provider to Google
          └── reversible production booking smoke test
```

Use the existing CI/CD pipeline. Do not add a Google service-account deployment path because Calendar API is already enabled manually.

## Current State

```text
OAuth web-client JSON ──valid──> /home/gift/Downloads/...apps.googleusercontent.com.json
                                         │
                                  callback URI matches

GitHub repository secrets
  [missing] GOOGLE_CLIENT_ID
  [missing] GOOGLE_CLIENT_SECRET
  [missing] GOOGLE_REDIRECT_URI
  [missing] GOOGLE_TOKEN_KEY
  [unset]   MEET_PROVIDER

CI + deploy       [green]
Production health [200 OK]
Google connection [not configured]
```

| Component | Current behavior | Target behavior |
|---|---|---|
| `.github/workflows/ci.yml` | Backend, frontend, E2E, and image builds pass | Unchanged |
| `.github/workflows/deploy.yml` | Passes Google runtime variables when secrets exist | Unchanged |
| Google OAuth | Connect button available; no runtime credentials | Teacher connects successfully |
| Meet provider | Fixed link | Google after successful consent and smoke test |
| Cancelled Google slot | Retains deleted event ID and dead Meet link | Clears both when the last active booking leaves |
| Legacy unused slot | May retain a fixed-mode link and bypass Google | Refreshes through the active provider when no booking is active |

## Goals

| Goal | Verification |
|---|---|
| Configure production OAuth without exposing credentials | Only secret names appear in command output |
| Keep rollback safe during rollout | Provider remains fixed until consent succeeds |
| Make booking cancellation reversible | A later booking creates a new Calendar event |
| Make the first Google-era booking authoritative | Unused legacy slots refresh; slots with active bookings retain their existing link |
| Prove Calendar event creation and deletion live | One future booking is created, observed, cancelled, and observed deleted |
| Preserve existing CI conventions | No workflow rewrite or new framework |

## Non-Goals

- Automating Google sign-in or consent.
- Creating a GCP project, OAuth client, or consent screen from Pulumi.
- Adding a service account or Workload Identity Federation for Calendar API enablement.
- Changing the OAuth scopes, callback route, booking policy, or notification behavior.
- Committing any credential, refresh token, or token-encryption key.

## Rollout Architecture

```text
Local OAuth JSON                         GitHub repository
┌──────────────────────┐                ┌─────────────────────────┐
│ web.client_id        │───────────────>│ GOOGLE_CLIENT_ID       │
│ web.client_secret    │───────────────>│ GOOGLE_CLIENT_SECRET   │
│ web.redirect_uris[0] │───────────────>│ GOOGLE_REDIRECT_URI    │
└──────────────────────┘                │ GOOGLE_TOKEN_KEY       │
                                        └───────────┬─────────────┘
openssl rand -base64 32 ────────────────────────────┘
                                                    │
                                                    ▼
                                      deploy: MEET_PROVIDER unset
                                                    │
                                           fixed-link backend
                                                    │
                                                    ▼
                              /admin → Connect Google → user consent
                                                    │
                                                    ▼
                                  status: connected=true, reconnect=false
                                                    │
                                                    ▼
                                  set MEET_PROVIDER=google → deploy
                                                    │
                                                    ▼
                                  book → inspect → cancel → inspect
```

## Secret Handling

| Value | Source | Destination | Rule |
|---|---|---|---|
| `GOOGLE_CLIENT_ID` | `web.client_id` in local JSON | GitHub secret | Pipe directly; never print |
| `GOOGLE_CLIENT_SECRET` | `web.client_secret` in local JSON | GitHub secret | Pipe directly; never print |
| `GOOGLE_REDIRECT_URI` | `web.redirect_uris[0]` in local JSON | GitHub secret | Validate exact URI: `https://lessons.giftmugweni.com/api/auth/google/callback` |
| `GOOGLE_TOKEN_KEY` | `openssl rand -base64 32` | GitHub secret | Generate once; retain for the life of stored tokens |
| `MEET_PROVIDER` | Operator action | GitHub secret | Leave unset until OAuth status is connected |

The OAuth JSON is a web-client download containing a nested `web` credential object. It is not a GCP service-account key and must not be assigned to `GCP_CREDENTIALS`.

## Lifecycle Correction

### Current Failure

```text
Create booking
      │
      ▼
Slot = { MeetLink: L, GoogleEventId: E }
Booking = Active
      │
      ├── cancel ───────────────► delete E, but Slot remains { L, E }
      │                              │
      │                              └── rebook skips provider and reuses dead L
      │
      └── reschedule away ──────► delete E, but old Slot remains { L, E }
```

### Target State

```text
Cancel or reschedule the last active booking
                    │
                    ▼
       Query other active bookings for slot
                    │
         ┌──────────┴──────────┐
         │ yes                 │ no
         ▼                     ▼
 retain link + event    clear MeetLink + GoogleEventId
                              │
                              ▼
                     delete Google event once
                              │
                              ▼
                    future booking creates new event
```

A shared slot remains intact when another student still has an active booking. A reschedule to the same date does not release the slot. When a new booking or reschedule enters a slot with no active bookings, the active provider refreshes any legacy fixed-mode link before the booking is saved.

## Components

| File or surface | Change |
|---|---|
| `backend/Application/Bookings/BookingSlotLifecycle.cs` | Clear a released slot, return its Google event ID, and identify active shared slots |
| `backend/Application/Bookings/CreateBookingCommandHandler.cs` | Refresh a legacy link when the destination slot has no active bookings |
| `backend/Application/Bookings/CancelBookingCommandHandler.cs` | Release a slot when cancellation removes its last active booking |
| `backend/Application/Bookings/RescheduleBookingCommandHandler.cs` | Release the old slot when its last booking moves; refresh an unused destination and delete the old event once |
| `backend/Tests/Bookings/BookingLifecycleTests.cs` | Add focused lifecycle regression coverage with in-memory persistence and recording fakes |
| GitHub repository secrets | Add four runtime secrets, then add `MEET_PROVIDER=google` after consent |
| Existing workflows | No source changes |

## Error Handling and Rollback

```text
OAuth or deploy failure          Provider rollback
┌──────────────────────┐        ┌─────────────────────────────┐
│ keep provider unset  │───────>│ fixed links remain usable  │
│ inspect run/site     │        │ token key is preserved     │
└──────────────────────┘        └─────────────────────────────┘

Google provider failure
┌──────────────────────┐        ┌─────────────────────────────┐
│ set provider=fixed   │───────>│ deploy fixed mode           │
│ redeploy             │        │ diagnose using safe logs    │
└──────────────────────┘        └─────────────────────────────┘
```

- Do not rotate `GOOGLE_TOKEN_KEY` while encrypted tokens exist; rotation would require reconnecting Google.
- Do not remove the Google client secrets during rollback; only the provider switch controls runtime behavior.
- OAuth errors stay in fixed mode and are diagnosed from GitHub Actions and sanitized application logs.
- External Calendar deletion is best-effort by design; clearing the slot prevents reuse of a known-deleted event.

## Testing Strategy

```text
Regression tests                    Repository checks                 Live smoke test
┌──────────────────────┐            ┌──────────────────────┐          ┌──────────────────────┐
│ cancel last booking  │            │ dotnet test          │          │ future booking      │
│ cancel shared slot   │───────────>│ frontend test        │─────────>│ unique Meet link     │
│ reschedule last      │            │ frontend typecheck   │          │ Calendar event seen │
│ reschedule shared    │            │ frontend build       │          │ cancel and delete   │
└──────────────────────┘            │ Playwright E2E       │          └──────────────────────┘
                                   └──────────────────────┘
```

The live booking uses a far-future, currently unused weekday. It executes the normal notification path. Twilio is currently unconfigured, so notification delivery is log-only and is not a Google rollout success criterion.

## Success Criteria

| Stage | Pass condition |
|---|---|
| Secrets | The four OAuth/token secret names exist before consent; `MEET_PROVIDER` is still unset; no values are printed or committed |
| Fixed-mode deploy | GitHub Actions deployment succeeds and `/health` returns `200` |
| OAuth | Admin shows connected with no reconnect requirement |
| Google-mode deploy | Deployment succeeds and `/health` remains `200` |
| Event creation | A real booking produces a unique Meet link and visible Calendar event |
| Event deletion | Cancelling removes the Calendar event and releases the slot |
| Regression | Last cancellation/reschedule clears stale Google fields; shared slots remain intact; unused legacy slots refresh |
| Repository | Existing CI remains green and the worktree contains no secrets |

## Assumptions

- Calendar API is already enabled manually in the Google Cloud project.
- The OAuth consent screen is published to Production and the teacher account is authorized.
- The user can complete Google sign-in and consent in the browser.
- The existing DigitalOcean, GHCR, Pulumi, and GitHub Actions deployment path remains available.
- A real test booking is acceptable; Twilio notification delivery is not required while Twilio remains unconfigured.
