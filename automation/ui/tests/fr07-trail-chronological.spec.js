const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');
const { parseDisplayTimestamp } = require('../pages/helpers');

// FR-07: trail table must be ordered oldest -> newest.
// Known defect: BUG-005 — trail points are not chronologically ordered.
test('FR-07 / TC-007A — trail table is ordered oldest to newest [BUG-005]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  await list.openOffender('Amar');

  const detail = new OffenderDetailPage(page);
  const raw = await detail.trailTimestamps();
  expect(raw.length).toBeGreaterThan(1);

  const parsed = raw.map(parseDisplayTimestamp);
  const sorted = [...parsed].sort((a, b) => a - b);

  expect(parsed.map((d) => d.getTime())).toEqual(sorted.map((d) => d.getTime()));
});
