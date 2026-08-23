# OffenderWatch — Automation

Automated regression suite for the OffenderWatch Monitoring Console
(`https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie`), covering
Part 3 of the QA assignment: UI automation (Playwright/JS) and API
automation (pytest + requests).

Every automated scenario traces back to a PRD requirement ID (FR-xx /
API-xx) and, where it documents a known defect, to a Bug ID from
`OffenderWatch_Tests.xlsx` (sheet **Bug Reports**). **A red test here is not
a broken test — it is the suite proving a real defect exists.** Each such
test has a `[BUG-xxx]` tag in its title and a comment explaining the
expected-vs-actual behavior.

```
automation/
  ui/    Playwright (JavaScript) — UI scenarios
  api/   pytest + requests (Python) — API scenarios
```

## UI automation (`automation/ui`)

### Install & run

```bash
cd automation/ui
npm install
npx playwright install chromium   # first time only
npm test                          # runs the whole suite headless
npm run test:headed               # watch it drive a real browser
npm run report                    # open the last HTML report
```

Results also land in `results/ui-results.json` (machine-readable, used by
the QA dashboard) and `playwright-report/` (HTML, with screenshots/traces
for every failure).

### Structure

- `pages/OffenderListPage.js`, `pages/OffenderDetailPage.js` — page objects
  for the offender list panel and the trail/detail panel.
- `pages/helpers.js` — parses the app's `DD/MM/YYYY, HH:mm` display format.
- `tests/*.spec.js` — one file per requirement area, named `frNN-*.spec.js`.
- `playwright.config.js` — `baseURL` points at the app; one worker, no
  retries (a red result should mean "defect confirmed," not "retry until
  green" — retrying would hide that signal).

### Scenarios (11 across 8 files)

| Spec file | Requirement | Expected outcome |
|---|---|---|
| `fr01-pagination.spec.js` | FR-01 | **Fails** — [BUG-001] pager under-reports page count |
| `fr02-search-case-insensitive.spec.js` | FR-02 | **Fails** — [BUG-013] search is case-sensitive |
| `fr03-create-validation.spec.js` (4 cases) | FR-03 | 1 passes (valid create), 3 fail — [BUG-002]/[BUG-014]/[BUG-003] |
| `fr04-edit-persistence.spec.js` | FR-04 | **Fails** — [BUG-017] Risk Level edit dropped from PUT payload |
| `fr07-trail-chronological.spec.js` | FR-07 | **Fails** — [BUG-005] trail table not chronological |
| `fr09-latest-reading.spec.js` | FR-09 | **Fails** — [BUG-006] Latest reading not from newest point |
| `fr10-location-validation.spec.js` | FR-10 | **Fails** — [BUG-007]/[BUG-009] invalid location accepted |
| `fr11-dashboard-stats.spec.js` | FR-11 | Currently **passes** on live data — see note below |

**Note on FR-11:** the original manual testing (Excel, BUG-011) observed UI
totals disagreeing with `/api/stats` by a wide margin (12 vs 24). Re-run
against the current live data, this automated check passes — the
discrepancy did not reproduce in this run. This does not clear BUG-011; it
means the manual finding needs a fresh repro (it may be tied to a specific
sequence of operations, e.g. right after a delete). BUG-011 stays **Open**
in the dashboard pending that retest.

## API automation (`automation/api`)

### Install & run

```bash
cd automation/api
pip install -r requirements.txt
pytest -v                          # console output
pytest -v --junitxml=results/api-results.xml   # machine-readable, for the dashboard
```

### Structure

- `conftest.py` — shared `session`/`base_url` fixtures and a `unique_national_id`
  generator (`AUTO<timestamp>`) so created test offenders are easy to find
  and clean up.
- `test_api01_paging_search.py` … `test_api05_stats.py` — one file per
  API-xx requirement.
- `cleanup_test_data.py` — deletes every offender whose National ID starts
  with `AUTO` (i.e. everything this suite created). Run it after a session
  if you want to tidy the shared demo environment; **not** required for the
  tests to pass.

### Scenarios (16 across 5 files, `test_api03` parametrized ×7)

| File | Requirement | Expected outcome |
|---|---|---|
| `test_api01_paging_search.py` | API-01 | paging metadata: **fails** — [BUG-001], confirmed at the API (floor instead of ceiling division); partial match: passes; case-insensitive: **fails** — [BUG-013] |
| `test_api02_status_codes.py` | API-02 | 201/204: pass; 404 for unknown offender/trail: **fail** — [BUG-012] |
| `test_api03_validation.py` | API-03 / FR-03 / FR-10 | **All fail** — [BUG-002]/[BUG-003]/[BUG-014]/[BUG-008]/[BUG-009]: every invalid payload tested (empty required field, duplicate ID, future DOB, bad enum, out-of-range location fields) is accepted with 2xx instead of rejected with 400 |
| `test_api04_trail_order.py` | API-04 | **Fails** — [BUG-016] trail not chronological at the API |
| `test_api05_stats.py` | API-05 | offender/active totals: pass; trail-point total after cascade delete: **fails** — [BUG-015] |

### Why so many red tests

The PRD (API-03) requires the same validation server-side as the UI
enforces. Testing directly against Swagger/the REST API (as the assignment
explicitly asks) shows that almost none of the FR-03/FR-10 business rules
are enforced at the API layer — only malformed JSON/type errors are
rejected. This is stronger evidence than UI testing alone: it proves the
defects are backend validation gaps, not just missing UI-side checks, since
a client bypassing the UI (e.g. a mobile app or a script) could persist
invalid offenders and location points today.

## Test data hygiene

Both suites create offenders with a `nationalId` starting with `AUTO` so
they're trivially identifiable and don't collide with the seeded data or
each other. Because several validation defects mean "reject this" scenarios
sometimes still persist a record, re-running the suites repeatedly will
accumulate a few extra offenders in the shared environment — this is itself
further evidence of the validation defects, not a flaw in the tests. Run
`automation/api/cleanup_test_data.py` to remove them.

## Mapping to the assignment deliverables

- **Part 3 minimums** (5 UI + 5 API scenarios): met — 11 UI scenarios across
  8 files, 16 API scenarios (7 of them parametrized cases of one test)
  across 5 files.
- **Assertions against PRD behavior**: every assertion message states the
  PRD-required behavior, not just the raw value comparison.
- **Clean structure**: page objects for UI, fixtures/conftest for API.
- **Honest handling of failing tests**: every test whose PRD-expected
  behavior currently fails carries a `[BUG-xxx]` tag in its title and a
  comment explaining the defect, cross-referenced to `OffenderWatch_Tests.xlsx`.
