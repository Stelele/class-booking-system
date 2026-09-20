import { defineConfig } from '@playwright/test'
import { rmSync } from 'node:fs'
import { resolve } from 'node:path'

const root = resolve(__dirname, '..')

// fresh SQLite file per run (wal/shm too — a stale WAL beside a new DB risks corruption)
rmSync(`${root}/data/e2e.db`, { force: true })
rmSync(`${root}/data/e2e.db-shm`, { force: true })
rmSync(`${root}/data/e2e.db-wal`, { force: true })

export default defineConfig({
  testDir: 'tests',
  timeout: 30_000,
  workers: 1, // shared DB — journey dates must not collide mid-flight
  use: { baseURL: 'http://localhost:5173' },
  webServer: [
    {
      // a previous run's servers can linger for a moment — wait for the ports first
      command: `i=0; while ss -ltn | grep -q ':8080 ' && [ $i -lt 30 ]; do sleep 0.5; i=$((i+1)); done; cd ${root}/backend && dotnet run --project Booking.Host --no-launch-profile`,
      url: 'http://localhost:8080/health',
      reuseExistingServer: false,
      timeout: 180_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'E2E',
        ASPNETCORE_URLS: 'http://localhost:8080',
        E2E: 'true',
        // absolute path: DbPath resolves relative to process cwd
        App__DbPath: `${root}/data/e2e.db`,
        App__FixedMeetLink: 'https://meet.google.com/e2e-test-link',
      },
    },
    {
      command: `i=0; while ss -ltn | grep -q ':5173 ' && [ $i -lt 30 ]; do sleep 0.5; i=$((i+1)); done; npm run dev`,
      url: 'http://localhost:5173',
      reuseExistingServer: false,
      timeout: 180_000,
      cwd: resolve(root, 'frontend'),
    },
  ],
})
