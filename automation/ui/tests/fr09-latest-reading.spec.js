const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');
const { parseDisplayTimestamp } = require('../pages/helpers');

// FR-09: "Latest reading" (speed/battery/signal) must come from the same
// trail point as "Last seen" (the most recent timestamp).
// Known defect: BUG-006 — Latest reading values come from an earlier point.
test('FR-09 / TC-009A-D — Latest reading matches the most recent trail point [BUG-006]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  await list.openOffender('Mizrahi');

  const detail = new OffenderDetailPage(page);

  const rowCount = await detail.trailRows.count();
  let newestIdx = 0;
  let newestTime = null;
  for (let i = 0; i < rowCount; i++) {
    const ts = (await detail.trailRows.nth(i).locator('td:nth-child(2)').textContent()).trim();
    const t = parseDisplayTimestamp(ts);
    if (!newestTime || t > newestTime) {
      newestTime = t;
      newestIdx = i;
    }
  }
  const newestRow = detail.trailRows.nth(newestIdx);
  const expectedSpeed = (await newestRow.locator('td:nth-child(5)').textContent()).replace('km/h', '').trim();
  const expectedBattery = (await newestRow.locator('td:nth-child(6)').textContent()).replace('%', '').trim();
  const expectedSignal = (await newestRow.locator('td:nth-child(7)').textContent()).trim();

  const actualSpeed = (await detail.latestSpeed.textContent()).trim();
  const actualBattery = (await detail.latestBattery.textContent()).trim();
  const actualSignal = (await detail.latestSignal.textContent()).trim();

  expect(actualSpeed, 'Latest reading speed should equal the newest trail point speed').toBe(expectedSpeed);
  expect(actualBattery, 'Latest reading battery should equal the newest trail point battery').toBe(expectedBattery);
  expect(actualSignal, 'Latest reading signal should equal the newest trail point signal').toBe(expectedSignal);
});
