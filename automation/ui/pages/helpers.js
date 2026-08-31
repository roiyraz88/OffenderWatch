// Parses the app's "DD/MM/YYYY, HH:mm" display format into a comparable Date.
function parseDisplayTimestamp(text) {
  const [datePart, timePart] = text.split(',').map((s) => s.trim());
  const [dd, mm, yyyy] = datePart.split('/').map(Number);
  const [hh, min] = timePart.split(':').map(Number);
  return new Date(yyyy, mm - 1, dd, hh, min);
}

// Creates a disposable offender (via the API) and posts the given location
// points to it, also via the API. Used so trail-related tests (FR-07, FR-09)
// don't depend on a specific seeded offender's trail data — that data has
// repeatedly gone stale or corrupted over the course of testing (see
// BUG-007/BUG-018). Points are posted in the given array order, so pass them
// out of chronological order to actually exercise ordering defects.
async function createOffenderWithTrail(request, baseURL, { lastNamePrefix = 'Trail', points }) {
  const nationalId = `AUTO${Date.now()}`;
  const created = await (
    await request.post(`${baseURL}/api/offenders`, {
      data: {
        firstName: 'Auto',
        lastName: `${lastNamePrefix}${Date.now() % 100000}`,
        nationalId,
        dateOfBirth: '1990-01-01',
        riskLevel: 'Low',
        status: 'Active',
      },
    })
  ).json();

  for (const point of points) {
    await request.post(`${baseURL}/api/offenders/${created.id}/locations`, { data: point });
  }

  return created;
}

module.exports = { parseDisplayTimestamp, createOffenderWithTrail };
