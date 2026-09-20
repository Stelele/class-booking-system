# Class Booking System

Evening programming lessons: shared booking calendar for 2 UK-based students + teacher (20:30 Africa/Harare, Mon–Sat).

## Dev quickstart

    # backend (http://localhost:8080)
    dotnet run --project backend/Host

    # frontend (http://localhost:5173, proxies /api -> 8080)
    cd frontend && npm install && npm run dev

    # e2e (starts both + runs Playwright)
    cd e2e && npm install && npx playwright install --with-deps && npm test

## Layout

    backend/   C# Minimal API (Clean Architecture)
    frontend/  Vue 3 + TS + Nuxt UI
    e2e/       Playwright journeys
    infra/     Pulumi C# + docker-compose deploy
    docs/      specs + plans

## Deploy

- Push to `main` → CI (backend, frontend, e2e) → GHCR images → Pulumi deploy over SSH to the droplet (nginx vhost + `docker compose up`).
- Domain: `lessons.giftmugweni.com` (TLS via certbot on the droplet).
- One-time droplet prep: docker + `docker login ghcr.io` (done 2026-09-20).
- Secrets live in GitHub repo secrets; R2 keys also in 1Password (`Lesson Secrets`).
- Login codes: emailed via Gmail SMTP (`SMTP_USER`/`SMTP_PASSWORD` secrets) — if SMTP is unset, codes print to `docker logs class-booking-backend-1`.
- Manual redeploy: Actions → deploy → Run workflow.
