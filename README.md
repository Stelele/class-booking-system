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
