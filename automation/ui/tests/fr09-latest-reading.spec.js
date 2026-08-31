const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');
const { parseDisplayTimestamp, createOffenderWithTrail } = require('../pages/helpers');

// FR-09: "Latest reading" (speed/battery/signal) must come from the same
// trail point as "Last seen" (the most recent timestamp).
// Known defect: BUG-006 — Latest reading values come from an earlier point.
//
// Uses its own disposable offender with 3 points posted out of chronological
// order (via the API), instead of depending on a specific seeded offender's
// trail — that data has repeatedly gone stale over the course of testing.
// The chronologically-latest point is posted FIRST, so if the app just
// echoes the last-inserted/last-returned point as "latest" instead of the
// max-timestamp one, this reliably catches it.
test('FR-09 / TC-009A-D — Latest reading matches the most recent trail point [BUG-006]', async ({
  page,
  request,
  baseURL,
}) => {
  const created = await createOffenderWithTrail(request, baseURL, {
    lastNamePrefix: 'Latest',
    points: [
      { timestamp: '2026-01-01T12:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 30, batteryPct: 60, signal: 5 },
      { timestamp: '2026-01-01T08:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 10, batteryPct: 80, signal: 2 },
      { timestamp: '2026-01-01T10:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 20, batteryPct: 70, signal: 3 },
    ],
  });

  try {
    const list = new OffenderListPage(page);
    await list.goto();
    await list.openOffender(created.lastName);

    const detail = new OffenderDetailPage(page);

    const rowCount = await detail.trailRows.count();
    expect(rowCount).toBe(3);
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
  } finally {
    await request.delete(`${baseURL}/api/offenders/${created.id}`);
  }
});
