# Google Login and Persistent Sessions Design

**Date:** 2026-09-25
**Status:** Approved design; implementation not started

## Decision

```text
Current                                      Target
┌──────────────────────────┐                ┌──────────────────────────────┐
│ Email code only          │                │ Email code + Google login   │
│ 30-day cookie            │ ─────────────► │ Same 30-day cookie           │
│ Ephemeral DP keys        │                │ Persisted DP key ring        │
│ Calendar OAuth separate  │                │ Login and Calendar separate  │
└──────────────────────────┘                └──────────────────────────────┘
```

Use the existing Google OAuth client for a separate server-side login flow. Match only verified Google email addresses to existing users; never create accounts automatically. Persist ASP.NET Data Protection keys in the existing persistent data volume so ordinary deployments do not invalidate sessions.

## Current State

```text
Email code ──► /api/auth/verify ──► 30-day cookie
                                      │
                                      └── key ring inside container filesystem
                                          └─ lost when container is replaced

Admin ──► /api/auth/google/start ──► calendar.events.owned
                                      └── separate Calendar grant
```

| Surface | Current | Target |
|---|---|---|
| Login methods | Email code only | Email code and Google login |
| Google login audience | No login flow | Existing teacher/admin and students only |
| Account creation | Seeded users only | Unchanged; no Google auto-provisioning |
| Role source | `User.Role` in SQLite | Unchanged; never trust Google role data |
| Session cookie | Persistent, 30 days | Unchanged |
| Cookie protection | Ephemeral Data Protection keys | Keys persisted in `booking-data` volume |
| Google Calendar | Admin-only `calendar.events.owned` flow | Unchanged and separate from login |

## Goals

```text
Goal                              Evidence
──────────────────────────────────────────────────────────────
Fast login                        “Continue with Google” on login page
Account safety                    Verified email must match existing User
Session continuity                Cookie survives container replacement
Least privilege                   Login requests only openid/email/profile
No coupling                       Login never requests Calendar scope
No new identity provider          Reuse current Google OAuth client
```

## Non-Goals

- Automatic signup or account creation for unknown Google accounts.
- Removing email-code login or making Google the only login method.
- Changing user roles, student invitations, or seeded account policy.
- Requesting Calendar, Gmail, Drive, profile-write, or other unrelated scopes during login.
- Combining Google login consent with the admin Calendar connection consent.
- Introducing Auth0 or another identity provider.
- Revoking Calendar access on ordinary app logout.
- Supporting multiple backend replicas without a shared key ring.

## Authentication Architecture

```text
┌──────────────────────┐
│ LoginView            │
│ Continue with Google │
└──────────┬───────────┘
           │ browser redirect
           ▼
┌──────────────────────────────────────────────┐
│ GET /api/auth/google/login/start             │
│ scope = openid email profile                 │
│ state = existing single-use 10-minute state  │
│ redirect = configured Google login callback  │
└──────────┬───────────────────────────────────┘
           ▼
┌──────────────────────────────────────────────┐
│ Google authorization                         │
│ returns code + state                         │
└──────────┬───────────────────────────────────┘
           ▼
┌──────────────────────────────────────────────┐
│ GET /api/auth/google/login/callback          │
│ 1. consume state                             │
│ 2. exchange code server-side                 │
│ 3. call Google user-info endpoint            │
│ 4. require verified email                    │
│ 5. normalize + match existing User           │
│ 6. issue existing app cookie                 │
└──────────┬───────────────────────────────────┘
           ▼
       /calendar
```

### OAuth boundary

| Concern | Decision |
|---|---|
| OAuth client | Reuse existing Google client ID and secret |
| Login scopes | `openid email profile` only |
| Calendar scopes | Existing `calendar.events.owned` only |
| Login callback | `/api/auth/google/login/callback` |
| Calendar callback | Existing `/api/auth/google/callback` |
| Login redirect config | New `Google:LoginRedirectUri` setting / `GOOGLE_LOGIN_REDIRECT_URI` deployment variable |
| Token handling | Exchange and user-info lookup happen only on the server |
| Account matching | Case-normalized `User.Email`; no new table or provider-specific user ID |
| Role/name | Read from the matched `User`; Google profile fields do not control authorization |
| Unknown account | Return a generic login error; do not create a user |
| Unverified email | Reject even if an account has the same email |
| State | Existing single-use state store, 10-minute expiry |
| Return path | Fixed local `/calendar`; no user-controlled redirect target |

## Google Login Flow

```text
Existing User found?
       │
       ├─ no ──► generic “not registered” error ──► email-code fallback
       │
       └─ yes
           │
           ├─ email_verified = false ──► reject
           │
           └─ email_verified = true
                    │
                    ▼
             issue cookie with:
             - app User.Id as NameIdentifier
             - app User.Name
             - app User.Role as Admin claim when applicable
             - persistent 30-day expiry
```

The app remains a private three-user booking system. Google identity is an authentication factor for an already-known account, not an account-provisioning system.

## UI

```text
┌──────────────────────────────────────┐
│ Log in                               │
│                                      │
│ [ Continue with Google ]              │
│                                      │
│ ─────────────── or ────────────────  │
│ Email                                │
│ [ Your email                   ]     │
│ [ Send code ]                         │
└──────────────────────────────────────┘
```

- Keep the email form visible as a fallback.
- The Google control performs a normal server-side navigation; no token or client secret enters JavaScript.
- Successful login returns to `/calendar`.
- Callback failure returns to `/login?google=error` with a sanitized alert.
- The existing admin Google Calendar card and Disconnect control remain separate.
- Logout clears the local app cookie only; Calendar revocation remains an explicit admin action.

## Persistent Session Architecture

```text
Container start
      │
      ▼
┌──────────────────────────────────────────────┐
│ ASP.NET Data Protection                     │
│ PersistKeysToFileSystem                      │
│ /app/data/data-protection-keys               │
└──────────┬───────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────┐
│ Docker named volume booking-data:/app/data  │
│ survives docker compose replacement         │
└──────────────────────────────────────────────┘
```

- Configure the key-ring directory from the existing `App:DbPath` directory.
- Use a stable application name so keys remain valid across image replacements.
- Keep `ExpireTimeSpan = 30 days` and `IsPersistent = true` unchanged.
- Keep keys out of logs, source control, and the SQLite backup file.
- If the persistent volume is lost or restored without its key directory, sessions intentionally reset; Google Calendar credentials and booking data are not deleted by this change.
- A first deployment after enabling persistence may invalidate cookies issued before the key ring existed. Subsequent deployments must not invalidate them.

## Security and Failure Handling

```text
Invalid/missing state       ──► reject; no token exchange
Google token exchange fails ──► generic login error
User-info request fails     ──► generic login error
email_verified != true      ──► reject
No matching User            ──► no account creation
Cookie signing key missing  ──► existing login flow cannot issue valid cookie
```

- Never log authorization codes, access tokens, client secrets, Data Protection keys, or complete student contact data.
- Keep Google login and Calendar consent separate so a student login never requests Calendar access.
- Use exact configured redirect URIs; do not accept arbitrary callback URLs.
- Do not use Google profile names or roles for application authorization.
- The email-code flow remains available if Google is unavailable.
- A Google login does not revoke or replace the separate Calendar refresh token.

## Testing Strategy

```text
Backend
├─ login authorization URL contains openid/email/profile
├─ login URL does not contain calendar scope
├─ state is required and single-use
├─ verified existing email issues cookie with app role
├─ unknown email is rejected without account creation
├─ unverified email is rejected
├─ Google callback failure returns sanitized redirect
├─ cookie survives a new host using the same key directory
└─ existing email-code and Calendar tests remain green

Frontend
├─ login page shows Continue with Google
├─ email fallback remains available
├─ Google error query shows sanitized alert
└─ successful Google callback lands on calendar

Repository
├─ dotnet tests
├─ frontend unit tests
├─ frontend typecheck + production build
├─ Playwright E2E
├─ GitHub CI + deploy + health
└─ forced container replacement keeps /auth/me authenticated
```

## Rollout

```text
1. Add Google login redirect URI
              │
              ▼
2. Add GOOGLE_LOGIN_REDIRECT_URI secret/config
              │
              ▼
3. Deploy login + persistent key ring
              │
              ▼
4. Existing cookies may reset once; users sign in again
              │
              ▼
5. Force a second container replacement
              │
              ▼
6. Verify /auth/me remains authenticated
              │
              ▼
7. Verify Google login and Calendar flows remain independent
```

Manual Google Console setup:

```text
Application type: Web application
Login redirect URI:
https://lessons.giftmugweni.com/api/auth/google/login/callback

Existing Calendar redirect URI remains:
https://lessons.giftmugweni.com/api/auth/google/callback
```

Deployment variables:

| Variable | Purpose |
|---|---|
| `GOOGLE_CLIENT_ID` | Existing shared OAuth client |
| `GOOGLE_CLIENT_SECRET` | Existing shared server-side secret |
| `GOOGLE_LOGIN_REDIRECT_URI` | New login callback URI |
| `GOOGLE_REDIRECT_URI` | Existing Calendar callback URI |

## Success Criteria

| Area | Pass condition |
|---|---|
| Google login | Existing teacher/admin and students can sign in with verified Google email |
| Account safety | Unknown or unverified Google identities cannot create or access accounts |
| Scope separation | Login requests no Calendar scope; Calendar flow remains admin-only |
| Session continuity | 30-day cookie remains valid after ordinary container replacement |
| UX | Google option and email-code fallback both work |
| Security | Server-side exchange, state validation, exact redirect, sanitized errors, no secret logging |
| Regression | Existing auth, Calendar, booking, privacy, and deployment tests remain green |

## Assumptions

- Production runs one backend container using the existing `booking-data` named volume.
- Google’s user-info endpoint is reachable from the backend.
- The existing seeded email addresses remain the canonical account identifiers.
- The Google OAuth client may have multiple authorized redirect URIs.
- A user who changes their Google account email can continue using the email-code fallback until an administrator updates the account email.
