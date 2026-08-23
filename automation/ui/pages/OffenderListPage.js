// Page object for the offender list panel (left side of the app).
class OffenderListPage {
  constructor(page) {
    this.page = page;
    this.searchInput = page.locator('input.search');
    this.addOffenderBtn = page.locator('button.btn.primary', { hasText: 'Add Offender' });
    this.rows = page.locator('table.grid tbody tr');
    this.pagerLabel = page.locator('.pager span');
    this.nextPageBtn = page.locator('.pager button', { hasText: 'Next' });
    this.prevPageBtn = page.locator('.pager button', { hasText: 'Prev' });
  }

  async goto() {
    // baseURL has a subpath (…/AQApplication/Roie); goto('/') would resolve
    // to the site root and drop it, so navigate to '' (relative, keeps path).
    await this.page.goto('');
    await this.page.waitForSelector('table.grid');
    // The table shell renders before the offenders fetch resolves, so the
    // pager briefly reads something like "Page 1 of 1 (0 results)". Wait
    // for the pager to actually report a real, non-zero result count
    // before letting callers read it.
    await this.page
      .locator('.pager span')
      .filter({ hasText: /\([1-9]\d* results\)/ })
      .waitFor({ state: 'visible' });
  }

  async search(text) {
    await this.searchInput.fill(text);
    await this.page.waitForTimeout(400); // debounce
  }

  async rowByLastName(lastName) {
    // Search first so the row is guaranteed to be on the current page,
    // regardless of how many offenders/pages currently exist.
    await this.search(lastName);
    return this.page.locator('table.grid tbody tr', { hasText: lastName }).first();
  }

  async openOffender(lastName) {
    const row = await this.rowByLastName(lastName);
    await row.click();
    await this.page.waitForSelector('.trail-view');
    // Trail rows may legitimately be zero (a freshly created offender), so
    // wait for the header instead of requiring at least one row to exist.
    await this.page.waitForSelector('.trail-table-header');
    await this.page.waitForTimeout(500); // let the map/leaflet render settle
  }

  async lastNamesOnPage() {
    const cells = await this.page.locator('table.grid tbody tr td.name-cell').allTextContents();
    // cell text is "Last, First"
    return cells.map((c) => c.split(',')[0].trim());
  }

  async openAddOffenderModal() {
    await this.addOffenderBtn.click();
    await this.page.waitForSelector('.modal-backdrop .modal');
    return new OffenderFormModal(this.page);
  }

  async openEditModal(lastName) {
    const row = await this.rowByLastName(lastName);
    await row.locator('button.btn.small').first().click();
    await this.page.waitForSelector('.modal-backdrop .modal');
    return new OffenderFormModal(this.page);
  }

  async deleteOffender(lastName, { accept }) {
    this.page.once('dialog', (dialog) => (accept ? dialog.accept() : dialog.dismiss()));
    const row = await this.rowByLastName(lastName);
    await row.locator('button.btn.small.danger').click();
    await this.page.waitForTimeout(500);
  }

  async statTotals() {
    // Top bar renders three stat tiles in a fixed order: Offenders, Active, Trail points.
    // The stats fetch can resolve slightly after the offender table does, so
    // wait for the tiles to actually be present before reading them.
    await this.page.locator('.stats .stat b').first().waitFor({ state: 'attached' });
    const values = await this.page.locator('.stats .stat b').allTextContents();
    return {
      offenders: Number(values[0]),
      active: Number(values[1]),
      trailPoints: Number(values[2]),
    };
  }
}

class OffenderFormModal {
  constructor(page) {
    this.page = page;
    this.modal = page.locator('.modal-backdrop .modal');
    this.firstName = this.modal.getByLabel('First name', { exact: false });
    this.lastName = this.modal.getByLabel('Last name', { exact: false });
    this.nationalId = this.modal.getByLabel('National ID', { exact: false });
    this.dob = this.modal.getByLabel('Date of birth', { exact: false });
    this.riskLevel = this.modal.getByLabel('Risk level', { exact: false });
    this.status = this.modal.getByLabel('Status', { exact: false });
    this.cancelBtn = this.modal.locator('button', { hasText: 'Cancel' });
    this.submitBtn = this.modal.locator('button[type="submit"]');
  }

  async fill({ firstName, lastName, nationalId, dob, riskLevel, status }) {
    if (firstName !== undefined) await this.firstName.fill(firstName);
    if (lastName !== undefined) await this.lastName.fill(lastName);
    if (nationalId !== undefined) await this.nationalId.fill(nationalId);
    if (dob !== undefined) await this.dob.fill(dob); // yyyy-mm-dd
    if (riskLevel !== undefined) await this.riskLevel.selectOption(riskLevel);
    if (status !== undefined) await this.status.selectOption(status);
  }

  async submit() {
    await this.submitBtn.click();
  }

  async isOpen() {
    return this.modal.isVisible();
  }
}

module.exports = { OffenderListPage, OffenderFormModal };
