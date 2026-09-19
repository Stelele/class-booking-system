# Class Booking System — Design Spec

Date: 2026-09-19
Status: Approved (brainstorm session)
Source: `Idea.md` + clarifications

---

## 1. Problem

Teacher (Zim, `Africa/Harare`) + 2 adult students (UK healthcare, `Europe/London`, busy shift workers). Scheduling lessons by chat is a headache.

```
┌─────────────┐   1 slot/day 20:30 Harare, 2h, Mon–Sat
│  Teacher    │◄──────────────────────────────────────┐
│  (admin)    │                                        │
└──────┬──────┘        ┌───────────────┐              │
       │  everyone sees │ Student A     │── book ─────┤
       │  all bookings  │ (NHS, UK)     │── cancel ───┤
       │  (FOMO)        └───────────────┘── reschedule┘
       │                ┌───────────────┐
       │                │ Student B     │── same ──────┘
       │                │ (private care)│
       │                └───────────────┘
       ▼
  Same-day double-book = auto-combined lesson (never blocked)
```

**Non-goals (YAGNI):** payments, lesson content, multi-teacher, enforcing the monthly combined lesson (only displayed, never enforced).

---

## 2. Delivery Phases

Phased-safe approach (Option A, approved).

```
Slice 1 (week 1)                      Slice 2 (week 2)
┌──────────────────────────┐          ┌──────────────────────────┐
│ Booking core (day-1 reqs)│          │ Google OAuth (prod mode) │
│ Magic-code auth (email)  │ ───────► │ auto-Meet + Calendar     │
│ Admin block calendar     │          │ Twilio WhatsApp trio     │
│ Fixed recurring Meet link│          │ (Mon / 08:00 / 20:00)    │
│ .ics download            │          │ WhatsApp code delivery   │
│ Pulumi + CI/CD + R2 bkup │          └──────────────────────────┘
└──────────────────────────┘
```

| Rule | Decision |
|---|---|
| Booking window | Up to 30 min before lesson start (loose & trusting) |
| Cancellation | Any time |
| Reschedule | Any time (new date validated like a fresh booking; booking row moves, original date kept in audit fields) |
| Double-book same day | Allowed → becomes combined session |
| Sundays | Always unavailable |
| Other unavailable days | Admin blocks via calendar UI |
| Combined lesson/month | Displayed only ("0 combined this month" hint), never enforced |

---

## 3. Architecture

```
                    lessons.<your-domain>  (existing domain, existing DO droplet)
                                      │
                          ┌───────────▼───────────┐
                          │  nginx (host or ctr)  │  TLS via existing setup
                          └─────┬─────────────┬───┘
                     /api/*     │             │    /*
                          ┌─────▼─────┐ ┌─────▼──────┐
                          │ backend   │ │ frontend   │
                          │ ASP.NET   │ │ Vue 3 + TS │
                          │ Minimal   │ │ Nuxt UI    │
                          │ API (C#)  │ │ SPA, nginx │
                          └─────┬─────┘ └────────────┘
                                │
        ┌───────────┬───────────┼────────────┬─────────────┐
        ▼           ▼           ▼            ▼             ▼
   SQLite EF    Hangfire    Google API   Twilio WA    Cloudflare R2
   (volume)    (SQLite)    (slice 2)     (slice 2)    (DB backups)
```

Clean Architecture layers, mirroring the erpnext-dashboard backend conventions:

```
backend/
├── Domain/            # entities, booking rules (no deps)
├── Application/       # commands/queries + handlers (MediatR), DTOs
├── Infrastructure/    # DbContext, EF, Google/Twilio/R2 clients, Hangfire
├── Endpoints/         # minimal API route groups
├── Host/              # Program, DI, config, auth wiring
└── Tests/             # xUnit
frontend/              # Vue 3 + TS + Nuxt UI (components/views/composables)
infra/                 # Pulumi C# (free tier) — see §8
```

---

## 4. Data Model (SQLite, all instants stored UTC)

```
Users ──< Bookings >── Slots          BlockedDays
 │                        │                │
 magic codes, roles       │ meet link,     │ click-to-block
                          │ google event   │ dates
AuthCodes (ephemeral)
```

| Table | Key fields | Notes |
|---|---|---|
| `Users` | Id, Name, Email, PhoneE164, Role (Admin/Student), TimeZone (IANA) | seeded: 1 admin + 2 students |
| `Slots` | Id, Date (Harare calendar day), Status, MeetLink, GoogleEventId | one row per day; `GoogleEventId` null until slice 2 |
| `Bookings` | Id, SlotId, StudentId, Status (Active/Cancelled), CreatedAt/UpdatedAt | multiple Active per Slot = combined |
| `BlockedDays` | Date (unique), Reason | Sundays computed at runtime, not stored |
| `AuthCodes` | UserId, CodeHash, ExpiresAt | 6-digit, 10-min TTL, deleted on use |

**Time rules (no manual offsets anywhere):**

- Lesson anchor: 20:30 `Africa/Harare`, duration fixed 2h.
- Storage: UTC instants (NodaTime or TimeZoneConverter for conversions).
- API: returns UTC ISO + `Africa/Harare` reference; frontend converts via `Intl.DateTimeFormat` with the viewer's zone → students see `19:30 London` (BST) / `18:30` (GMT) automatically.
- Google Calendar events carry `timeZone: Africa/Harare` → each viewer sees their own local time natively.

---

## 5. API Surface

```
Auth:    POST /auth/request-code    { email or phone }
         POST /auth/verify          { code } → 30-day HttpOnly cookie
         POST /auth/logout
Slots:   GET  /slots?month=YYYY-MM  → availability + bookings (names, avatars)
Bookings:POST /bookings             { date }        (≤30min-before rule)
         POST /bookings/{id}/reschedule { newDate }
         DELETE /bookings/{id}      (cancel)
Admin:   GET/POST/DELETE /admin/blocked-days
         GET  /admin/backups        (status, last R2 backup)
Misc:    GET  /slots/{date}/ics     (calendar file, fixed Meet link)
         GET  /health
```

**Invariants (Domain, unit-tested):**
- No booking on Sunday or blocked day → reject with clear error.
- No booking within 30 min of start (admin exempt) → reject.
- Cancel/reschedule allowed by owning student or admin only.
- Students book only for themselves; admin may book/cancel on behalf of any student.
- Reschedule = validate new date, move booking, keep history of original date.

---

## 6. Auth

```
enter email/phone ──► 6-digit code ──► WhatsApp (slice 2) / email (slice 1)
                        │ 10-min TTL, hashed
                        ▼
                  verify → HttpOnly session cookie (30 days)
                  roles: Admin (teacher) | Student
```

- 3 users total, seeded. No registration, no password resets.
- Session: signed cookie (no server session table needed).

---

## 7. Frontend (Vue 3 + TS + Nuxt UI)

```
┌────────────────────────────────────────────────┐
│ Calendar (default view)          [My Lessons]  │
│ ┌────┐┌────┐┌────┐┌────┐┌────┐┌────┐┌────┐     │
│ │Mon ││Tue ││Wed ││Thu ││Fri ││Sat ││Sun │     │
│ │19:30││ —  ││👤A ││👤B ││A+B ││19:30││ ░  │     │
│ │London││    ││book││book││comb││    ││off │     │
│ └────┘└────┘└────┘└────┘└────┘└────┘└────┘     │
│  green=free  avatars=booked (FOMO)  grey=off   │
│  "no combined lesson yet this month" hint      │
└────────────────────────────────────────────────┘
  My Lessons: upcoming/past list + Cancel / Reschedule
  Admin: block calendar (click date), backups status
  Static: / (homepage), /privacy, /terms   ← Google prod-toggle reqs
```

- Every time shown is auto-localized to the viewer; Harare reference shown alongside.
- Booking = click a free day → confirm modal (shows local + Harare time + Meet link).
- Empty week view still shows the friendly "book something" prompt (pairs with Monday WhatsApp).

---

## 8. Infrastructure & Deploy (existing droplet — no new droplet)

```
GitHub (main, protected, PRs only, CodeRabbit must-pass)
   │ green CI = build + test (xUnit, Vitest)
   ▼
GH Actions: docker build/push (GHCR)
   ▼
SSH deploy to existing droplet (compose pull + up -d)
   ▼
Pulumi C# (free tier, local state file) manages:
   • nginx vhost + TLS for lessons.<domain>
   • backend + frontend containers (GHCR images)
   • volume mounts (SQLite, certs)
   • firewall rules (80/443 only)
```

**SQLite backups → Cloudflare R2:**

```
nightly 02:00 Harare:  sqlite backup API → gzip → R2 put (30-day retention)
container start:       if DB missing/empty → restore newest R2 snapshot → migrate
admin page:            last backup time + manual "restore" button (confirmation required)
```

---

## 9. Integrations

### 9.1 Google Meet + Calendar (slice 2) — `IMeetProvider`

```
           ┌────────────────────────────┐
           │ IMeetProvider              │
           │  CreateAsync(slot)         │
           │  UpdateAsync / CancelAsync │
           └─────┬───────────────┬──────┘
        slice 1 │               │ slice 2
   ┌────────────▼───┐   ┌───────▼──────────────────────┐
   │ FixedLink      │   │ GoogleCalendarProvider       │
   │ (recurring link│   │ OAuth (prod, unverified OK)  │
   │  from config)  │   │ events.insert?confDataVer=1  │
   └────────────────┘   │ hangoutsMeet, 20:30 +2h      │
                        │ timeZone Africa/Harare       │
                        └──────────────────────────────┘
        on invalid_grant → fallback FixedLink + admin "Reconnect Google" banner
```

**Google OAuth setup checklist (in spec, done once, day 1 of slice 2):**

| # | Step |
|---|---|
| 1 | Ship homepage `/`, `/privacy`, `/terms` on verified domain (slice 1 — already live) |
| 2 | Verify domain in Google Search Console |
| 3 | OAuth consent screen: fill branding URLs, **Publish app → In production** (stays unverified — fine for single user) |
| 4 | Scopes: `calendar.events` only |
| 5 | One-time consent by teacher → store refresh token server-side (encrypted at rest) |
| 6 | Re-consent after publishing (Testing-era tokens keep the 7-day clock) |
| 7 | Monthly keep-alive token refresh job |

### 9.2 Twilio WhatsApp (slice 2)

```
Jobs (Hangfire, SQLite storage, Africa/Harare schedule):
  Mon 09:00  weekly summary per student
             ├─ has lessons → list days (UK local times) + links
             └─ empty week  → friendly nudge + booking site link
  daily 08:00 lesson today → "today 19:30 your time" + Meet link
  daily 20:00 lesson in 30 min → reminder + Meet link
Immediate: booking confirmed / cancelled / rescheduled (with new time)
```

- Messages use Twilio approved templates (approval lead time is why slice 2, not slice 1).
- Every message renders times in the recipient's `Europe/London` via shared TZ helper.

---

## 10. Testing & Quality

| Layer | Tool | Covers |
|---|---|---|
| Domain | xUnit | booking rules table in §2/§5 (every row) |
| API | xUnit `WebApplicationFactory` | auth flow, endpoints, invariants, TZ edges (BST flip) |
| Frontend | Vitest + Vue Test Utils | composable TZ display, calendar states |
| Review | CodeRabbit | PR-blocking |
| CI | GH Actions | all tests green → deploy |

Branch protection: no direct pushes to `main`, PRs only, CI + CodeRabbit required.

---

## 11. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Google publish blocked (branding/domain) | slice 1 ships without Google; fixed Meet link keeps lessons running |
| `invalid_grant` returns (revocation, scope change) | fallback to FixedLink + reconnect banner, never block booking |
| Twilio template approval slow | email magic codes in slice 1; WhatsApp joins when approved |
| SQLite corruption / droplet loss | R2 nightly + restore-on-boot + admin manual restore |
| UK/Zim clock drift confusion | UTC storage + IANA everywhere; zero manual offsets; BST covered by tests |
| Free Gmail 3-participant Meet limit (60 min) | sessions are 2–3 participants (1 teacher + ≤2 students) — unaffected |

---

## 12. Repo Layout

```
class-booking-system/
├── backend/          # C# solution (Domain/Application/Infrastructure/Endpoints/Host/Tests)
├── frontend/         # Vue 3 + TS + Nuxt UI
├── infra/            # Pulumi C# + docker-compose + deploy scripts
├── .github/          # workflows (ci, deploy)
├── docs/superpowers/ # specs + plans
├── README.md
└── LICENSE.md
```
