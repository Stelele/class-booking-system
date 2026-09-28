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

## Withdraw a frontend build

Each deploy keeps the last 3 builds' hashed chunks so a tab that is mid-navigation
survives the swap (`infra/Program.cs`). Those assets are public and stay reachable
under their original URLs, so withdrawing a build needs two steps on the droplet —
and order matters, because the next deploy re-injects whatever the stash still holds.

    ssh <user>@<host>
    cd /opt/class-booking
    rm -rf retained-assets/*                        # 1. drop the stash
    docker compose up -d --force-recreate frontend   # 2. discard injected files

`restart` is not enough: retained chunks are `docker cp`'d into the container's
writable layer, so they survive a restart and only go away when the container is
recreated. A later deploy re-stashes whatever is running, so a *withdrawn* build
returns unless its stash is gone — hence step 1 before step 2.

Cloudflare may still hold withdrawn chunks at the edge for up to 2h
(`edge_cache_ttl`). Purge by exact URL (Cache → Purge → Custom Purge) to
withdraw immediately; the browser caches are not affected by a purge, and
clients holding a withdrawn build recover on their next reload.
