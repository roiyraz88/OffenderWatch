const { test, expect } = require('@playwright/test');
const { OffenderListPage } = require('../pages/OffenderListPage');

// FR-04: editing one field must persist exactly that change while every
// untouched field keeps its previous value.
// Known defect: BUG-017 — the Edit form's PUT payload omits riskLevel
// entirely (confirmed via network capture), so a Risk Level change is
// silently dropped even though the modal shows the new value pre-submit.
test('FR-04 / TC-004A — editing Risk Level only leaves other fields unchanged [BUG-017]', async ({ page }) => {
  const list = new OffenderListPage(page);
  await list.goto();

  const targetLastName = 'Gabay';
  let modal = await list.openEditModal(targetLastName);
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

  modal = await list.openEditModal(targetLastName);
  await expect(modal.firstName).toHaveValue(before.firstName);
  await expect(modal.lastName).toHaveValue(before.lastName);
  await expect(modal.nationalId).toHaveValue(before.nationalId);
  await expect(modal.dob).toHaveValue(before.dob);
  await expect(modal.status).toHaveValue(before.status);
  await expect(modal.riskLevel).toHaveValue('High');
});
