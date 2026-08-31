// Part 5 (Step 7 / TM-06) — explicit test-data-creation registration for
// Playwright specs.
//
// A Playwright Reporter runs in a different process than the test/worker
// code, so there is no shared in-memory state to intercept centrally the
// way the pytest side does via a response hook. testInfo.attach() is
// Playwright's own sanctioned worker -> reporter data channel; the
// reporter (ow-event-reporter.js) reads these attachments back out in
// onTestEnd() and emits the same test_data_created OW_EVENT contract the
// API suite uses.
//
// Call this ONLY immediately after the target application's own response
// confirms an entity was actually created — never speculatively, never for
// UI-only state, never for an edit of an existing record.
const ATTACHMENT_NAME = 'ow-test-data-created';

async function registerOffenderCreated(testInfo, { id, nationalId }) {
  await testInfo.attach(ATTACHMENT_NAME, {
    body: JSON.stringify({ entityType: 'Offender', entityExternalId: String(id), entityIdentifier: nationalId ?? null }),
    contentType: 'application/json',
  });
}

module.exports = { ATTACHMENT_NAME, registerOffenderCreated };
