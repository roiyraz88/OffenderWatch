// @ts-check
const { defineConfig, devices } = require('@playwright/test');

// Part 5 (Step 4 / TM-02) injects the target via OFFENDERWATCH_BASE_URL so
// the orchestrator can run this suite against any configured Environment's
// BaseUrlSnapshot. No hard-coded fallback: if it's missing, fail loudly and
// immediately rather than silently hitting the wrong (or no) target.
const baseURL = process.env.OFFENDERWATCH_BASE_URL;
if (!baseURL) {
  throw new Error(
    'OFFENDERWATCH_BASE_URL is required (no hard-coded fallback). Set it to ' +
      'the target OffenderWatch base URL before running this suite, e.g.:\n' +
      '  OFFENDERWATCH_BASE_URL=https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie ' +
      'npx playwright test',
  );
}

module.exports = defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45000,
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'results/ui-results.json' }],
    // Part 5 (Step 4) — structured OW_EVENT protocol for the orchestrator.
    ['./reporters/ow-event-reporter.js'],
  ],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
