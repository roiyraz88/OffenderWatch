const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-11: top-bar totals must be live and accurate, i.e. consistent with the
// API's own /api/stats and /api/offenders totals.
// Known defect: BUG-011 — UI top-bar totals disagree with the API.
test('FR-11 / TC-011A — UI top-bar totals match GET /api/stats [BUG-011]', async ({ page, request, baseURL }) => {
  const list = new OffenderListPage(page);
  await list.goto();

  const uiTotals = await list.statTotals();

  const res = await request.get(`${baseURL}/api/stats`);
  expect(res.ok()).toBeTruthy();
  const apiStats = await res.json();

  expect(uiTotals.offenders, 'UI offender total should match API totalOffenders').toBe(apiStats.totalOffenders);
  expect(uiTotals.active, 'UI active total should match API activeOffenders').toBe(apiStats.activeOffenders);
  expect(uiTotals.trailPoints, 'UI trail-point total should match API totalLocationPoints').toBe(
    apiStats.totalLocationPoints
  );
});
