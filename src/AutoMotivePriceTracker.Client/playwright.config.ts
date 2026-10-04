import { defineConfig } from '@playwright/test';
import path from 'node:path';
import { dotnetConnectionString, searchIndex, searchUrl } from './e2e/db';

const API_PORT = 5099;
const WEB_PORT = 4299;
const webUrl = `http://localhost:${WEB_PORT}`;
const root = path.resolve(__dirname, '../..');

// Isolated stack: its own API (port 5099, its own PostgreSQL database and OpenSearch index, scraping disabled so nothing
// touches real retailers) and an Angular dev server pointed at it (environment.e2e.ts).

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false, // tests share one seeded database
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: webUrl,
    // Uses the installed Edge so no browser download is needed. Override with E2E_CHANNEL=chrome,
    // or set E2E_CHANNEL= (empty) to use Playwright's bundled Chromium after `npx playwright install chromium`.
    channel: process.env['E2E_CHANNEL'] ?? 'msedge',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: [
    {
      command: `dotnet run -c Release --no-launch-profile --project "${path.join(root, 'src/AutoMotivePriceTracker.Web')}"`,
      url: `http://localhost:${API_PORT}/api/admin/scrape/status`,
      reuseExistingServer: false,
      timeout: 180_000,
      env: {
        ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Default: dotnetConnectionString,
        Scraping__Enabled: 'false',
        Search__Url: searchUrl,
        Search__Index: searchIndex,
        Cors__AllowedOrigins__0: webUrl,
      },
    },
    {
      command: `npx ng serve --configuration e2e --port ${WEB_PORT}`,
      url: webUrl,
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
});
