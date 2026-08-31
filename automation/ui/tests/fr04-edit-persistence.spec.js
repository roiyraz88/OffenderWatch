const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-04: editing one field must persist exactly that change while every
// untouched field keeps its previous value.
// Known defect: BUG-017 — the Edit form's PUT payload omits riskLevel
// entirely (confirmed via network capture), so a Risk Level change is
// silently dropped even though the modal shows the new value pre-submit.
//
// Uses its own disposable offender instead of a specific seeded name, so the
// test doesn't depend on which seeded offenders currently exist / are intact.
test('FR-04 / TC-004B — editing Risk Level only leaves other fields unchanged [BUG-017]', async ({
  page,
  request,
  baseURL,
}) => {
  const lastName = `Persist${Date.now() % 100000}`;
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
    const list = new OffenderListPage(page);
    await list.goto();

    let modal = await list.openEditModal(lastName);
    const before = {
      firstName: await modal.firstName.inputValue(),
      lastName: await modal.lastName.inputValue(),
      nationalId: await modal.nationalId.inputValue(),
      dob: await modal.dob.inputValue(),
      status: await modal.status.inputValue(),
    };

    await modal.riskLevel.selectOption('High');
    await modal.submit();
    await page.waitForTimeout(600);

    modal = await list.openEditModal(lastName);
    await expect(modal.firstName).toHaveValue(before.firstName);
    await expect(modal.lastName).toHaveValue(before.lastName);
    await expect(modal.nationalId).toHaveValue(before.nationalId);
    await expect(modal.dob).toHaveValue(before.dob);
    await expect(modal.status).toHaveValue(before.status);
    await expect(modal.riskLevel).toHaveValue('High');
  } finally {
    await request.delete(`${baseURL}/api/offenders/${created.id}`);
  }
});
