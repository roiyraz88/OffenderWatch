const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');

// FR-10: signal must be within 1-5. Invalid values must be rejected with a
// clear message and nothing saved.
// Known defect: BUG-007 — invalid input fails without a clear validation
// message (verified here for signal=6).
test('FR-10 / TC-010E — signal outside 1-5 is rejected without saving a point [BUG-007]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  await list.openOffender('Peretz');

  const detail = new OffenderDetailPage(page);
  const countBefore = await detail.trailRows.count();

  await detail.openAddLocationForm();
  await detail.fillLocation({ lat: 32.08, lon: 34.78, speed: 10, battery: 50, signal: 6 });

  const errors = [];
  page.on('pageerror', (e) => errors.push(e));
  await detail.submitLocation();
  await page.waitForTimeout(600);

  const countAfter = await detail.trailRows.count();

  expect(errors, 'submitting an invalid location must not throw an unhandled client error').toHaveLength(0);
  expect(countAfter, 'an out-of-range signal value must not be persisted as a trail point').toBe(countBefore);
});
