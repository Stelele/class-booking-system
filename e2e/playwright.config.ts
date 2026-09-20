import { defineConfig } from '@playwright/test'
import { resolve } from 'node:path'

const root = resolve(__dirname, '..')

// NOTE: do NOT delete data/e2e.db at config-module scope — Playwright workers
// re-evaluate this module mid-run and would delete the live database under the
// running backend. The fresh-db rm lives in the backend webServer command below.

export default defineConfig({
  testDir: 'tests',
  timeout: 30_000,
  workers: 1, // shared DB — journey dates must not collide mid-flight
  use: { baseURL: 'http://localhost:5173' },
  webServer: [
    {
      // SIGKILL any previous instance FIRST — a gracefully-dying server still
      // answers /health during shutdown, and Playwright would run the whole
      // suite against a zombie whose DB file the command below deletes
      command: `bash ${root}/e2e/kill-servers.sh 8080 && rm -f ${root}/data/e2e.db ${root}/data/e2e.db-shm ${root}/data/e2e.db-wal && cd ${root}/backend && dotnet run --project Booking.Host --no-launch-profile > /tmp/e2e-be.log 2>&1`,
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
      command: `bash ${root}/e2e/kill-servers.sh 5173 && npm run dev`,
      url: 'http://localhost:5173',
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'pipe',
      cwd: resolve(root, 'frontend'),
    },
  ],
})
