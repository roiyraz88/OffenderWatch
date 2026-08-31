const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');
const { registerOffenderCreated } = require('../reporters/test-data-capture');

function uniqueId() {
  return `AUTO${Date.now()}`;
}

// FR-03: create requires first/last name + unique national ID + past DOB;
// invalid input must be rejected with nothing saved.

test('FR-03 / TC-003A — create offender with fully valid data succeeds', async ({ page, request, baseURL }, testInfo) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const nid = uniqueId();
  const lastName = `Zzz${Date.now() % 100000}`;

  try {
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
  } finally {
    const res = await request.get(`${baseURL}/api/offenders`, { params: { search: lastName, pageSize: 10 } });
    const { items } = await res.json();
    for (const o of items) {
      // Registered right where its real, target-app-confirmed existence is
      // known — before this test's own cleanup deletes it (Step 7 / TM-06).
      await registerOffenderCreated(testInfo, o);
      await request.delete(`${baseURL}/api/offenders/${o.id}`);
    }
  }
});

test('FR-03 / TC-003B — creation is rejected when Last Name is empty [BUG-002]', async ({ page, request, baseURL }, testInfo) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const nid = uniqueId();

  try {
    const modal = await list.openAddOffenderModal();
    await modal.fill({ firstName: 'NoLast', lastName: '', nationalId: nid, dob: '1990-01-01' });
    await modal.submit();
    await page.waitForTimeout(500);

    // Expected: modal stays open with an error and nothing is saved.
    expect(await modal.isOpen(), 'Add Offender modal should remain open after an invalid submit').toBe(true);
  } finally {
    // BUG-002 means this often DOES get saved despite the empty field —
    // clean it up via its National ID so it doesn't pollute the shared app.
    const res = await request.get(`${baseURL}/api/offenders`, { params: { search: nid, pageSize: 10 } });
    const { items } = await res.json();
    for (const o of items) {
      await registerOffenderCreated(testInfo, o);
      await request.delete(`${baseURL}/api/offenders/${o.id}`);
    }
  }
});

test('FR-03 / TC-003D — creation is rejected for a duplicate National ID [BUG-014]', async ({ page, request, baseURL }, testInfo) => {
  const list = new OffenderListPage(page);
  await list.goto();

  // Creates its own disposable offender to duplicate against, instead of
  // hardcoding a specific seeded offender's National ID — that record isn't
  // guaranteed to still exist/be intact (seed data has repeatedly been
  // recreated/corrupted over the course of testing).
  const existingNid = `AUTO${Date.now()}`;
  const original = await (
    await request.post(`${baseURL}/api/offenders`, {
      data: {
        firstName: 'Original',
        lastName: `Orig${Date.now() % 100000}`,
        nationalId: existingNid,
        dateOfBirth: '1990-01-01',
        riskLevel: 'Low',
        status: 'Active',
      },
    })
  ).json();
  await registerOffenderCreated(testInfo, original);

  try {
    const modal = await list.openAddOffenderModal();
    await modal.fill({
      firstName: 'Dup',
      lastName: 'Licate',
      nationalId: existingNid,
      dob: '1990-01-01',
      riskLevel: 'Low',
      status: 'Active',
    });
    await modal.submit();
    await page.waitForTimeout(600);

    await list.search('Licate');
    const rows = await list.lastNamesOnPage();
    expect(rows, 'an offender with a duplicate National ID must not be persisted').not.toContain('Licate');
  } finally {
    const res = await request.get(`${baseURL}/api/offenders`, { params: { search: 'Licate', pageSize: 50 } });
    const { items } = await res.json();
    for (const o of items) {
      await registerOffenderCreated(testInfo, o);
      await request.delete(`${baseURL}/api/offenders/${o.id}`);
    }
    await request.delete(`${baseURL}/api/offenders/${original.id}`);
  }
});

test('FR-03 / TC-003E — creation is rejected for a future Date of Birth [BUG-003]', async ({ page, request, baseURL }, testInfo) => {
  const list = new OffenderListPage(page);
  await list.goto();
  const lastName = `Future${Date.now() % 100000}`;

  try {
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
  } finally {
    // BUG-003 means this often DOES get saved despite the future DOB —
    // clean it up so it doesn't pollute the shared app.
    const res = await request.get(`${baseURL}/api/offenders`, { params: { search: lastName, pageSize: 10 } });
    const { items } = await res.json();
    for (const o of items) {
      await registerOffenderCreated(testInfo, o);
      await request.delete(`${baseURL}/api/offenders/${o.id}`);
    }
  }
});
