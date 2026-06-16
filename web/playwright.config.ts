import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright smoke E2E config.
 *
 * ASSUMPTION: the .NET API is already running on http://localhost:5001 with
 * seeded dev data (companies, vendors, etc.). E2E does NOT start the backend —
 * bring it up first (e.g. `docker compose up -d` / `dotnet run`) and verify with
 * `curl http://localhost:5001/api/ping`. See web/AGENTS.md (E2E section).
 *
 * The Vite dev server IS managed here via `webServer` below (reused if already
 * running). The frontend talks to the API through VITE_API_BASE_URL.
 *
 * Port: `E2E_PORT` (default 5173) drives BOTH the baseURL and the dev server, so
 * they stay consistent. If 5173 is taken by another (possibly broken) dev
 * server, run e.g. `E2E_PORT=5180 pnpm test:e2e` to start a clean one.
 * `E2E_BASE_URL` can override the URL entirely (e.g. to hit a remote preview).
 */
const PORT = process.env.E2E_PORT ?? '5173';
const BASE_URL = process.env.E2E_BASE_URL ?? `http://localhost:${PORT}`;

export default defineConfig({
  testDir: './e2e',
  // These smoke tests share ONE backend + seeded DB and several of them MUTATE it
  // (baixa, conciliação confirm, classify). Running them in parallel races on the
  // same rows and flakes (a different test fails each run). Force a single worker
  // so the mutating flows run serially against a stable dataset.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'list',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  // Reuses an already-running dev server on PORT; otherwise starts one there.
  // `--strictPort` so the baseURL above always matches (Vite won't silently fall
  // back to another port if PORT is busy).
  webServer: {
    command: `pnpm dev --port ${PORT} --strictPort`,
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
