const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

function uniqueId() {
  return `AUTO${Date.now()}`;
}

// FR-03: create requires first/last name + unique national ID + past DOB;
// invalid input must be rejected with nothing saved.

test('FR-03 / TC-003A — create offender with fully valid data succeeds', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const nid = uniqueId();
  const lastName = `Zzz${Date.now() % 100000}`;

  const modal = await list.openAddOffenderModal();
  await modal.fill({
    firstName: 'Auto',
    lastName,
    nationalId: nid,
    dob: '1990-05-15',
    riskLevel: 'Medium',
    status: 'Active',
  });
  await modal.submit();
  await page.waitForTimeout(600);

  await list.search(lastName);
  const rows = await list.lastNamesOnPage();
  expect(rows).toContain(lastName);
});

test('FR-03 / TC-003B — creation is rejected when Last Name is empty [BUG-002]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const nid = uniqueId();

  const modal = await list.openAddOffenderModal();
  await modal.fill({ firstName: 'NoLast', lastName: '', nationalId: nid, dob: '1990-01-01' });
  await modal.submit();
  await page.waitForTimeout(500);

  // Expected: modal stays open with an error and nothing is saved.
  expect(await modal.isOpen(), 'Add Offender modal should remain open after an invalid submit').toBe(true);
});

test('FR-03 / TC-003D — creation is rejected for a duplicate National ID [BUG-014]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();

  // "305412876" belongs to seeded offender David Cohen.
  const modal = await list.openAddOffenderModal();
  await modal.fill({
    firstName: 'Dup',
    lastName: 'Licate',
    nationalId: '305412876',
    dob: '1990-01-01',
    riskLevel: 'Low',
    status: 'Active',
  });
  await modal.submit();
  await page.waitForTimeout(600);

  await list.search('Licate');
  const rows = await list.lastNamesOnPage();
  expect(rows, 'an offender with a duplicate National ID must not be persisted').not.toContain('Licate');
});

test('FR-03 / TC-003E — creation is rejected for a future Date of Birth [BUG-003]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const lastName = `Future${Date.now() % 100000}`;

  const modal = await list.openAddOffenderModal();
  await modal.fill({
    firstName: 'Future',
    lastName,
    nationalId: uniqueId(),
    dob: '2099-01-01',
    riskLevel: 'Low',
    status: 'Active',
  });
  await modal.submit();
  await page.waitForTimeout(600);

  await list.search(lastName);
  const rows = await list.lastNamesOnPage();
  expect(rows, 'an offender with a future DOB must not be persisted').not.toContain(lastName);
});
