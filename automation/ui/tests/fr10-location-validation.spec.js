const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');

// FR-10: signal must be within 1-5. Invalid values must be rejected with a
// clear message and nothing saved.
// Known defects: BUG-007 (the value is accepted without validation) and
// BUG-018 (the specific out-of-range signal crashes the whole app — blank
// page, unhandled RangeError, for as long as the offender's poisoned trail
// data persists). Confirmed via console errors and by GET
// /api/offenders/{id} going empty afterward for offenders this was run
// against directly (id=1, id=10 earlier today).
//
// Because that crash is destructive to whichever offender it's run
// against, this test creates and deletes its own disposable offender via
// the API rather than reusing a shared/seeded one.
test('FR-10 / TC-010E — signal outside 1-5 is rejected without saving a point [BUG-007 / BUG-018]', async ({
  page,
  request,
  baseURL,
}) => {
  const nationalId = `AUTO${Date.now()}`;
  const created = await (
    await request.post(`${baseURL}/api/offenders`, {
      data: {
        firstName: 'Loc',
        lastName: `Test${Date.now() % 100000}`,
        nationalId,
        dateOfBirth: '1990-01-01',
        riskLevel: 'Low',
        status: 'Active',
      },
    })
  ).json();

  try {
    const list = new OffenderListPage(page);
    await list.goto();
    await list.openOffender(created.lastName);

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
  } finally {
    await request.delete(`${baseURL}/api/offenders/${created.id}`);
  }
});
