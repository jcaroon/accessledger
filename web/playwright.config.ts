import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: false,
  workers: 1,
  use: { baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:5080', viewport: { width: 1440, height: 1050 }, trace: 'retain-on-failure', screenshot: 'only-on-failure',
    launchOptions: process.env.BROWSER_PATH ? { executablePath: process.env.BROWSER_PATH, args: ['--no-sandbox', '--disable-dev-shm-usage'] } : {} },
  webServer: process.env.E2E_BASE_URL ? undefined : { command: 'dotnet run --project ../server --urls http://127.0.0.1:5080', url: 'http://127.0.0.1:5080/api/health', reuseExistingServer: !process.env.CI, env: { ASPNETCORE_ENVIRONMENT: 'Development' }, timeout: 120000 }
});
