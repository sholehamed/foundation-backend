import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: ['**/tenant-bff-real.spec.ts', '**/tenant-first-login-real.spec.ts'],
  timeout: 180_000,
  expect: { timeout: 45_000 },
  retries: 0,
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    ignoreHTTPSErrors: true,
    baseURL: 'https://e2e-http-ci.shinera.localhost:8443',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
