# GCP console checklist (one-time, ~10 min, human)

Slice 2A: Google auto-Meet. Pulumi cannot create the OAuth consent-screen web client (no IaC resource exists), so the project + client are manual; Pulumi/CI only enables the Calendar API (idempotent).

## 1. Project + API

1. console.cloud.google.com → New project `class-booking-prod` (No organization), note the project ID.
2. APIs & Services → Enable **Google Calendar API** (also done by CI when `GCP_PROJECT_ID` is set — the console step is belt-and-braces).

## 2. OAuth consent screen

3. OAuth consent screen → **External** → app name `Lesson Booking`, support email.
4. Authorized domain: `giftmugweni.com`.
5. App links (must be hosted on the verified domain — they are):
   - Homepage: `https://lessons.giftmugweni.com/`
   - Privacy: `https://lessons.giftmugweni.com/privacy`
   - Terms: `https://lessons.giftmugweni.com/terms`
6. Add yourself (`giftmugweni@gmail.com`) as a **test user**.
7. Verify the domain in Search Console if Google asks (verifying account must be project Owner/Editor).
8. **Publish to Production** (unverified is fine for a 1-user app: warning screen + 100-user cap only).

## 3. OAuth client

9. Credentials → Create OAuth client ID → **Web application** → redirect URI:
   `https://lessons.giftmugweni.com/api/auth/google/callback`
10. Copy the Client ID + Client Secret.

## 4. Secrets + first connect

11. Generate the token key: `openssl rand -base64 32`.
12. Set GitHub secrets:
    - `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` (from step 10)
    - `GOOGLE_TOKEN_KEY` (from step 11)
    - `GOOGLE_REDIRECT_URI` = `https://lessons.giftmugweni.com/api/auth/google/callback`
    - Leave `MEET_PROVIDER` **unset** until the connect flow succeeds (app stays on the fixed link).
13. Optional (CI API enablement): create a service account with `Service Usage Admin` on the project, download the JSON key, set it as the `GCP_CREDENTIALS` secret + `GCP_PROJECT_ID` secret. Deploy then enables Calendar API automatically.
14. Deploy (any merge to main). Open `/admin` → **Connect Google** → sign in as giftmugweni@gmail.com.
15. Banner clears → `gh secret set MEET_PROVIDER --body google` → next deploy switches bookings to per-lesson Meet links.

## 5. Critical caveat

If you ever consented while the app was in **Testing**, that refresh token dies in 7 days even after publishing. Revoke at myaccount.google.com → Third-party access, then reconnect AFTER Publish. (The app's own fallback covers this: bookings keep working on the fixed link + a Reconnect banner appears.)
