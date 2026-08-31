const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-02: search must be partial and case-insensitive.
// Known defect: BUG-013 — search is case-sensitive (confirmed at the API layer too).
//
// Uses its own disposable offender with a distinctive last name instead of a
// seeded name, so the test doesn't depend on which seeded offenders
// currently exist / are searchable (several have been corrupted over the
// course of testing — see BUG-007/BUG-018's evidence).
test('FR-02 / TC-002C — search returns same results regardless of letter casing [BUG-013]', async ({
  page,
  request,
  baseURL,
}) => {
  // All-lowercase on purpose: neither .toLowerCase() nor .toUpperCase() of a
  // *mixed*-case name would exactly match its stored casing under a
  // case-sensitive backend, so BOTH searches would spuriously return empty
  // and the sanity check below would fail for the wrong reason. Starting
  // from all-lowercase guarantees the lowercase search is an exact-case
  // match no matter how the app matches casing.
  const lastName = `casetest${Date.now() % 100000}`;
  const created = await (
    await request.post(`${baseURL}/api/offenders`, {
      data: {
        firstName: 'Auto',
        lastName,
        nationalId: `AUTO${Date.now()}`,
        dateOfBirth: '1990-01-01',
        riskLevel: 'Low',
        status: 'Active',
      },
    })
  ).json();

  try {
    const substring = lastName.slice(0, 8); // "casetest" — matches the stored casing exactly
    const list = new OffenderListPage(page);
    await list.goto();

    await list.search(substring); // exact-case match — must find it regardless of any bug
    const exactCaseResults = await list.lastNamesOnPage();
    expect(exactCaseResults, 'sanity check: the created offender must be findable at all').toContain(lastName);

    await list.search(substring.toUpperCase());
    const upperResults = await list.lastNamesOnPage();

    expect(upperResults, 'an uppercase search should return the same offender as the exact-case search').toEqual(
      exactCaseResults,
    );
  } finally {
    await request.delete(`${baseURL}/api/offenders/${created.id}`);
  }
});
