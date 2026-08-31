const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { OffenderDetailPage } = require('../pages/OffenderDetailPage');
const { parseDisplayTimestamp, createOffenderWithTrail } = require('../pages/helpers');

// FR-07: trail table must be ordered oldest -> newest.
// Known defect: BUG-005 — trail points are not chronologically ordered.
//
// Uses its own disposable offender with 3 points posted out of chronological
// order (via the API), instead of depending on a specific seeded offender's
// trail — that data has repeatedly gone stale over the course of testing.
test('FR-07 / TC-007A — trail table is ordered oldest to newest [BUG-005]', async ({ page, request, baseURL }) => {
  const created = await createOffenderWithTrail(request, baseURL, {
    lastNamePrefix: 'Chrono',
    points: [
      { timestamp: '2026-01-01T10:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 20, batteryPct: 70, signal: 3 },
      { timestamp: '2026-01-01T08:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 10, batteryPct: 80, signal: 2 },
      { timestamp: '2026-01-01T12:00:00Z', lat: 32.05, lon: 34.78, speedKmh: 30, batteryPct: 60, signal: 5 },
    ],
  });

  try {
    const list = new OffenderListPage(page);
    await list.goto();
    await list.openOffender(created.lastName);

    const detail = new OffenderDetailPage(page);
    const raw = await detail.trailTimestamps();
    expect(raw.length).toBe(3);

    const parsed = raw.map(parseDisplayTimestamp);
    const sorted = [...parsed].sort((a, b) => a - b);

    expect(parsed.map((d) => d.getTime())).toEqual(sorted.map((d) => d.getTime()));
  } finally {
    await request.delete(`${baseURL}/api/offenders/${created.id}`);
  }
});
