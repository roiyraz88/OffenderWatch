const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-02: search must be partial and case-insensitive.
// Known defect: BUG-013 — search is case-sensitive (confirmed at the API layer too).
test('FR-02 / TC-002C — search returns same results regardless of letter casing [BUG-013]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();

  await list.search('coh'); // lowercase substring of "Cohen"
  const lowerResults = await list.lastNamesOnPage();

  await list.search('Coh'); // capitalized substring
  const capResults = await list.lastNamesOnPage();

  expect(lowerResults, 'lowercase search should return the same offender as the mixed-case search').toEqual(capResults);
  expect(capResults).toContain('Cohen');
});
