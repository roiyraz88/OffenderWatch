// Page object for the trail/detail panel (right side, shown after selecting an offender).
class OffenderDetailPage {
  constructor(page) {
    this.page = page;
    this.root = page.locator('.trail-view');
    this.title = this.root.locator('.detail-header h2');
    this.meta = this.root.locator('.detail-header .meta');
    this.latestSpeed = this.root.locator('.latest-grid > div').nth(0).locator('b');
    this.latestBattery = this.root.locator('.latest-grid > div').nth(1).locator('b');
    this.latestSignal = this.root.locator('.latest-grid > div').nth(2).locator('b');
    this.lastSeen = this.root.locator('.latest-time');
    this.trailRows = this.root.locator('table.trail-grid tbody tr');
    this.addLocationBtn = this.root.locator('.trail-table-header button', { hasText: 'Add location' });
    this.locForm = this.root.locator('form.loc-form');
    this.locLat = this.locForm.locator('input[placeholder="Latitude"]');
    this.locLon = this.locForm.locator('input[placeholder="Longitude"]');
    this.locSpeed = this.locForm.locator('input[placeholder="Speed km/h"]');
    this.locBattery = this.locForm.locator('input[placeholder="Battery %"]');
    this.locSignal = this.locForm.locator('input[placeholder="Signal 1-5"]');
    this.locSubmit = this.locForm.locator('button[type="submit"]');
  }

  async trailTimestamps() {
    const cells = await this.trailRows.locator('td:nth-child(2)').allTextContents();
    return cells.map((t) => t.trim());
  }

  async openAddLocationForm() {
    await this.addLocationBtn.click();
    await this.page.waitForSelector('form.loc-form');
  }

  async fillLocation({ lat, lon, speed, battery, signal }) {
    if (lat !== undefined) await this.locLat.fill(String(lat));
    if (lon !== undefined) await this.locLon.fill(String(lon));
    if (speed !== undefined) await this.locSpeed.fill(String(speed));
    if (battery !== undefined) await this.locBattery.fill(String(battery));
    if (signal !== undefined) await this.locSignal.fill(String(signal));
  }

  async submitLocation() {
    await this.locSubmit.click();
  }
}

module.exports = { OffenderDetailPage };
