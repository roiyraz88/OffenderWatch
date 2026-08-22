const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-01: pages = ceil(total / 5) and every offender must be reachable.
// Known defect: BUG-001 — pager exposes only 2 pages regardless of total.
test('FR-01 / TC-001C, TC-001D — pager page count matches ceil(total / 5) and all offenders reachable [BUG-001]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();

  const label = (await list.pagerLabel.textContent()).trim(); // "Page 1 of 2 (13 results)"
  const match = label.match(/Page \d+ of (\d+) \((\d+) results\)/);
  expect(match, `Unexpected pager label format: "${label}"`).not.toBeNull();
  const totalPagesShown = Number(match[1]);
  const totalResults = Number(match[2]);

  const expectedPages = Math.ceil(totalResults / 5);

  const seen = new Set();
  let guard = 0;
  while (guard < 20) {
    guard++;
    for (const name of await list.lastNamesOnPage()) seen.add(name);
    if (await list.nextPageBtn.isDisabled()) break;
    await list.nextPageBtn.click();
    await page.waitForTimeout(300);
  }

  // BUG-001: pager reports 2 pages but ceil(total/5) requires more, and not
  // every offender counted in `totalResults` is actually reachable via paging.
  expect(totalPagesShown, 'pager total pages should equal ceil(total / pageSize)').toBe(expectedPages);
  expect(seen.size, 'every offender counted in the result total must be reachable through paging').toBe(totalResults);
});
