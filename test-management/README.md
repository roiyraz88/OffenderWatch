# OffenderWatch — Test Management Platform (Part 5)

Status: **COMPLETE.** Every TM-01..TM-08 requirement in the official
assignment is implemented, tested, and verified against the real
OffenderWatch application. See [`PART5_PLAN.md`](../PART5_PLAN.md) at the
repo root for the full step-by-step implementation history and
verification record.

| Requirement | What it is | Where |
|---|---|---|
| TM-01 | Environment configuration | `Controllers/EnvironmentController.cs`, `Services/EnvironmentService.cs`, `client/src/pages/EnvironmentsPage.tsx` |
| TM-02 | Real run execution & management | `Services/RunOrchestrator.cs`, `RunService.cs`, `Controllers/RunController.cs`, `client/src/pages/RunsPage.tsx`/`RunDetailPage.tsx` |
| TM-03 | Real-time progress | `Hubs/RunHub.cs`, `client/src/hooks/useRunLiveUpdates.ts` |
| TM-04 | Test history (Regression/Recovery/flakiness) | `Services/HistoryClassifier.cs`, `TestHistoryService.cs`, `client/src/pages/TestsPage.tsx`/`TestDetailPage.tsx` |
| TM-05 | Persistence | SQLite + EF Core throughout (`Data/TestManagementDbContext.cs`, migrations) — every Run/ScenarioResult/EvidenceArtifact/TestDataRecord survives an app restart; see [Persistence](#persistence-tm-05) below |
| TM-06 | Test-data ownership & cleanup | `Services/TestDataService.cs`, `automation/*/test_data_capture.*`, `client/src/pages/TestDataPage.tsx` |
| TM-07 | Dynamic Go/No-Go dashboard | `Services/DashboardService.cs`, `client/src/pages/DashboardPage.tsx` |
| TM-08 | Evidence capture & retrieval | `RunOrchestrator.HandleArtifactCreatedAsync`, `Controllers/EvidenceController.cs`, `automation/*/evidence_capture.*` |

## Structure

- `server/` — ASP.NET Core Web API (.NET 8, C#)
- `server.Tests/` — xUnit backend tests (temp-SQLite-per-test, never touch
  `data/testmanagement.db` or the real OffenderWatch app)
- `client/` — React + Vite + TypeScript
- `data/testmanagement.db` — the final submission SQLite database (committed —
  see [Final submission data](#final-submission-data))
- `artifacts/` — the matching evidence files (screenshots, logs, API
  request/response JSON, Playwright traces) for that same database

## Architecture

```
React
  |
  | POST /api/runs { environmentId }
  v
ASP.NET Core API  ── creates TestRun (Queued), enqueues RunId, returns 202
  |
  v
RunQueue (in-memory Channel<int>)
  |
  v
RunExecutionBackgroundService (single consumer)
  |
  +--> RunOrchestrator (own DbContext, one per run)
          |
          +--> pytest   (automation/api)   ─┐  sequential:
          +--> Playwright (automation/ui)  ─┘  API suite, then UI suite
          |
          +--> parses OW_EVENT|{json} lines from each child process's stdout
          |
          +--> persists TestCases (reused by stable ExternalId) +
               ScenarioResults (one per TestCase per Run) into SQLite
```

The HTTP request that starts a run never blocks on the automation suites —
`POST /api/runs` returns as soon as the `TestRun` row exists and its id is
enqueued. All actual execution happens in the background worker, which
creates its own DI scope (and therefore its own `DbContext`) per run.

**Step 5 (TM-03) added SignalR** on top of the exact same `OW_EVENT` stream
the orchestrator already parsed in Step 4 — no change to the runner
integration itself. Full flow:

```
pytest / Playwright
  |
  | OW_EVENT|{json}
  v
RunOrchestrator
  |
  +--> SQLite (persist — the source of truth, always first)
  |
  +--> RunHub group "run:{runId}" (broadcast — a notification on top)
          |
          v
        React (Run Details page, subscribed to that one run's group)
```

## Real-time (TM-03 / Step 5)

**Hub**: `Hubs/RunHub.cs`, mapped at `/hubs/runs`. Deliberately thin — no
execution logic, only two methods (`SubscribeToRun(runId)` /
`UnsubscribeFromRun(runId)`) that add/remove the caller's connection from a
per-run SignalR group named `run:{runId}` (`RunHub.GroupName`). The browser
may only subscribe/unsubscribe; it can never mutate a Run or ScenarioResult
over the Hub — Start/Stop stay on the existing REST endpoints.

**Broadcast points** — `RunOrchestrator` (and, for the two direct-stop
paths, `RunService`) call `IHubContext<RunHub>` **after** the corresponding
`SaveChangesAsync` has committed, never before:
- `RunUpdated`: Queued→Running, and the final Running→{Completed/Failed/
  Stopped}.
- `ScenarioUpdated`: a scenario's creation as Queued, Queued→Running, and
  Running→final status (including Cancelled, on Stop).

Both payloads reuse the exact same DTOs (`RunSummaryDto`/
`ScenarioResultDto`) the REST API already returns (`Services/
RunDtoMapper.cs` is the one mapper both transports share) — no EF entity is
ever sent, and the live message shape matches the REST shape exactly.

**Broadcasting is fail-soft.** Every send is wrapped and logged, never
thrown — a SignalR transport failure (or zero connected clients, the normal
case for most of a run's life) can never affect a run's persisted status or
crash the run. Verified directly with a unit test using a hub context whose
every send throws (`RealTimeTests.SignalRTransportFailure_DoesNotMarkRunFailed`).

**React** (`hooks/useRunLiveUpdates.ts`, used by `RunDetailPage.tsx`):
1. `GET /api/runs/{id}` hydrates the page first — REST remains necessary;
   a completed historical run is fully viewable with zero live connection.
2. A SignalR connection is opened (`@microsoft/signalr`,
   `withAutomaticReconnect()`) to `${VITE_API_BASE_URL}/hubs/runs` (never a
   hard-coded address) and subscribes to that run's group.
3. From then on, `RunUpdated`/`ScenarioUpdated` messages are applied as
   incremental in-place updates to local state — the run header/totals
   update, and a scenario row is found by id and replaced (or appended if
   it wasn't in the initial REST snapshot yet). No full REST reload happens
   per event.
4. To avoid missing a fast transition during page setup, the connection is
   established and subscribed *before* the REST fetch resolves is relied
   on — on connect (and again on every reconnect) the page triggers a fresh
   REST fetch, so the authoritative database state is always what's
   eventually shown regardless of event-arrival timing. No event replay is
   implemented — the database is always sufficient.
5. A small "Live / Reconnecting… / Disconnected" indicator in the page
   header reflects connection state. On reconnect, the client re-subscribes
   to the run's group and re-fetches REST state automatically.

The pre-existing manual **Refresh** button remains as a fallback/debug
action; TM-03's acceptance criterion (watching a run's scenarios transition
without ever pressing it) does not depend on it.

## Run flow & status semantics

A `TestRun` moves through `Queued -> Running -> {Completed | Stopped |
Failed}`.

- **Completed** means the suites ran their full structured lifecycle —
  it says nothing about whether the tests inside it passed. A Completed run
  routinely has `FailedCount > 0` and `ExpectedFailedCount > 0`; that's
  expected, not an error.
- **Failed** is reserved for *infrastructure* problems: the runner process
  couldn't start, or it exited without ever emitting a `suite_finished`
  event (crashed mid-run). A non-zero pytest/Playwright process exit code
  from real test failures is normal and never makes the Run itself Failed.
- **Stopped** means a user cancelled it via `POST /api/runs/{id}/stop`.

Each `ScenarioResult` (one per TestCase per Run) has its own, separate
status: `Queued -> Running -> {Passed | Failed | ExpectedFail | Skipped |
Cancelled}`. **Failed vs ExpectedFail** is the one place this platform
actually classifies something (`Services/ScenarioClassifier.cs`): a
scenario that fails *and* whose `TestCase` has known-defect metadata
(a `BugId`, e.g. `BUG-001`) is stored as `ExpectedFail`; a failure with no
such metadata is `Failed`. A known-defect scenario that unexpectedly
*passes* is stored as `Passed` (not silently hidden). `Cancelled` is only
ever used for scenarios still `Queued`/`Running` when a Stop lands.

## Start / Stop

`POST /api/runs { "environmentId": 1 }` — the Environment is the *only*
source of the target URL; the request body never carries a raw `BaseUrl`.
Its name and URL are frozen onto the new `TestRun` row
(`EnvironmentNameSnapshot`/`BaseUrlSnapshot`) at creation time, so later
renaming, editing, or even deleting that Environment never changes what
that historical run says it ran against.

`POST /api/runs/{id}/stop`:
- **Queued** run — flipped straight to `Stopped`; it never starts.
- **Running** run — the orchestrator's cancellation token is signalled, the
  active child process (pytest or Playwright) is killed with its whole
  process tree, the second suite never starts if the first was still
  running, every `ScenarioResult` still `Queued`/`Running` for that Run
  becomes `Cancelled`, and everything already `Passed`/`Failed`/
  `ExpectedFail` before the stop is left untouched.
- Stopping a `Completed`/`Stopped`/`Failed` run returns **409 Conflict** —
  it never silently no-ops or corrupts history.

## The OW_EVENT protocol

Both suites are unaware of Part 5's HTTP API — no backend calls live inside
any pytest test or Playwright spec. Instead, a small reporter in each suite
(`automation/api/ow_event_reporter.py`, a pytest plugin;
`automation/ui/reporters/ow-event-reporter.js`, a Playwright reporter)
prints one line per event to stdout:

```
OW_EVENT|{"version":1,"eventType":"scenario_finished","runner":"pytest","timestampUtc":"...","externalId":"api::test_api01_paging_search.py::test_search_is_partial_match","status":"passed","durationMs":169,...}
```

Event types: `scenario_discovered`, `scenario_started`, `scenario_finished`,
`suite_finished`. The orchestrator (`Services/OwEvent.cs`) only ever looks
for lines containing the `OW_EVENT|` marker — every other line (pytest's
own `-v` output, Playwright's `list` reporter, stack traces, `console.log`
from a page under test, anything) is ordinary runner output and is simply
ignored, never parsed as data. This is why the exact same suites still
produce their normal, readable console/HTML/JSON output when run by hand —
the platform's integration is purely additive.

**Stable identity** (`ExternalId`) is what makes TM-04 history possible
later: `api::<pytest nodeid>` for pytest (e.g.
`api::test_api03_validation.py::test_create_offender_rejects_empty_last_name`)
and `ui::<spec file>::<test title>` for Playwright. Never a generated GUID,
never anything run-specific — the same scenario in Run 1 and Run 50
resolves to the same `TestCase` row, which is reused (not recreated) every
run.

**RequirementId/BugId metadata** is extracted non-invasively from what
already exists in this repo: each suite's own test docstrings /
Playwright test titles (this repo's own `FR-xx`/`API-xx` and `BUG-xxx`
conventions), not invented. If a test's title/docstring names no bug, its
`TestCase.BugId` stays null and a failure there is `Failed`, not
`ExpectedFail`.

## Target URL injection (OFFENDERWATCH_BASE_URL)

Neither automation suite hard-codes a target anymore. The orchestrator
passes `OFFENDERWATCH_BASE_URL=<TestRun.BaseUrlSnapshot>` to each child
process's environment — the *frozen* run snapshot, never a live re-read of
the Environment record, so a run stays reproducible even if the Environment
is edited/deleted while it's executing. See `automation/README.md` for how
to set this same variable to run either suite standalone from the command
line (both fail immediately with a clear error if it's unset — there is no
fallback).

## Runner configuration

`appsettings.json`'s `Runner` section (`Services/RunnerOptions.cs`) holds
every path/command as a *relative* value — nothing here is a hard-coded
absolute path in code. The orchestrator resolves them against the server's
own `ContentRootPath` at runtime:

```json
"Runner": {
  "RepoRootRelativeToContentRoot": "../..",
  "PythonExecutable": "python",
  "PytestWorkingDirectory": "automation/api",
  "PytestArguments": "-m pytest -v",
  "PlaywrightExecutableRelativePath": "node_modules/.bin/playwright.cmd",
  "PlaywrightWorkingDirectory": "automation/ui",
  "PlaywrightArguments": "test"
}
```

`PlaywrightExecutableRelativePath`'s default (`node_modules/.bin/playwright.cmd`)
is Windows-specific — swap it for `node_modules/.bin/playwright` to run the
orchestrator on macOS/Linux.

## Run locally — full reproducible setup

Everything below assumes a clean machine. Commands are given for
PowerShell/Bash from the repo root unless a `cd` is shown; adjust for your
shell as needed. No step here depends on this specific machine — every
path the platform itself uses is relative (see [Runner
configuration](#runner-configuration)).

> **Prefer one command?** `docker compose up --build` from the repo root
> does all of this for you — see **Bonus B-05 — One-command startup** under
> [Bonus features](#bonus-features) below. Everything in this section is
> still the normal, non-Docker workflow and remains fully supported.

### 1. Prerequisites

- **.NET 8 SDK** (`dotnet --version` should print `8.x`). If not installed:
  `winget install Microsoft.DotNet.SDK.8` (Windows) or the equivalent for
  your OS.
- **Node.js 18+** and **npm** (for both the React client and the
  Playwright suite).
- **Python 3.10+** and **pip** (for the pytest suite).
- **`dotnet-ef`** (only needed if you want to create/apply migrations
  yourself — a migration already exists and is committed):
  `dotnet tool install --global dotnet-ef`

### 2. Automation suites (required for the platform to actually run anything)

```bash
cd automation/api
pip install -r requirements.txt

cd ../ui
npm install
npx playwright install chromium   # downloads the browser binary (not committed to git)
```

Both suites are also independently runnable from the command line — see
[`automation/README.md`](../automation/README.md). Neither is hard-coded
to any target: both require `OFFENDERWATCH_BASE_URL` and fail immediately
with a clear error if it's unset.

### 3. The Part 5 server

```bash
cd test-management/server
dotnet restore
dotnet build
dotnet ef database update   # only needed if data/testmanagement.db doesn't already exist —
                             # the committed final-submission database already has the schema applied
dotnet run
```

Listens on `http://localhost:5174` (see
`server/Properties/launchSettings.json`); Swagger at `/swagger`, health
check at `/api/health`. The `Runner` section of `appsettings.json`
controls where the server looks for `automation/api` and `automation/ui`
relative to itself (`RepoRootRelativeToContentRoot`, default `../..`) — no
change needed if you cloned the repo as-is. **Windows vs. macOS/Linux**:
`Runner:PlaywrightExecutableRelativePath` defaults to
`node_modules/.bin/playwright.cmd` (the Windows shim); on macOS/Linux
change it to `node_modules/.bin/playwright` in `appsettings.json` (or an
environment-specific override) before starting a run.

### 4. The React client

```bash
cd test-management/client
npm install
cp .env.example .env.local   # sets VITE_API_BASE_URL=http://localhost:5174
npm run dev
```

Listens on `http://localhost:5173` — matches the server's `ClientOrigins`
CORS config in `server/appsettings.json`.

### 5. Backend tests (fast, deterministic, no real network calls)

```bash
cd test-management/server.Tests
dotnet test
```

Every test uses its own throwaway temp-file SQLite database and, where
HTTP is involved (TM-06 cleanup), a fake `HttpClient` handler — nothing
here ever touches `data/testmanagement.db` or the real OffenderWatch app.

### Using the platform

Open `http://localhost:5173/environments`, add an Environment pointing at
a real OffenderWatch instance (e.g.
`https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie`), then go
to `/runs` and click **Start New Run**. You'll land on the run's detail
page and watch it update live via SignalR — no manual refresh needed
(**Refresh** remains available as a fallback) — or click **Stop** while
it's Queued/Running. `/tests` shows history/Regression/Recovery/flakiness
per test; `/test-data` shows and cleans up automation-owned application
data; `/` is the dynamic Dashboard.

Full database schema rationale is documented in `PART5_PLAN.md`'s Step 2
section.

## Persistence (TM-05)

There is no dedicated "persistence feature" separate from the rest of the
platform — TM-05 is satisfied by the same SQLite + EF Core foundation
every other requirement is built on (established Step 2, reused
unchanged by every step since): every `TestRun`, `ScenarioResult`,
`TestCase`, `EvidenceArtifact`, and `TestDataRecord` is written through EF
Core to `test-management/data/testmanagement.db`, a real on-disk file, not
an in-memory store — closing and restarting the server (or the whole
machine) does not lose anything. The schema is fully reproducible from the
one committed EF Core migration (`Migrations/
20260831143752_InitialTestManagementSchema`), never created by hand.
Verified directly for this submission: the server was stopped and
restarted multiple times across Step 9's real-run session, and the
Dashboard/Runs/Tests/Test Data pages continued to show exactly the same
historical data before and after each restart.

## Test history (TM-04 / Step 6)

Every value on `/tests` and `/tests/:id` is **derived on read** from the
existing stable `TestCase` -> `ScenarioResult` -> `TestRun` data — there is
no separate History table, and nothing is duplicated or persisted. The
derivation lives in `Services/HistoryClassifier.cs` (pure, unit-tested) and
`Services/TestHistoryService.cs` (loads a TestCase's ScenarioResults
ordered by their owning Run's `CreatedAtUtc` — runs execute strictly
sequentially, Step 4, so run-creation order is execution order).

**Transitions** (`GET /api/tests/{id}/history`): each chronological result
is classified against the *previous comparable* result — Passed is
success-like, Failed/ExpectedFail are failure-like, Skipped/Cancelled are
neutral and are skipped when looking backward (they never manufacture a
false Regression/Recovery): `FirstResult` (no earlier comparable result),
`Regression` (success-like -> failure-like), `Recovery` (failure-like ->
success-like), `StillFailing`, `StillPassing`.

**CurrentFailureSince**: the Run where the test's *current, unbroken*
failure-like streak began, walked forward chronologically — a Passed
result resets it to null; Skipped/Cancelled never break or start a streak.
Null whenever the latest comparable result isn't failure-like.

**LastPass**: the most recent Run+timestamp where the TestCase passed, or
null if it has never passed.

**Flakiness** (`IsFlaky`, Bonus **B-01**): among the **last 10 comparable**
(non-neutral) results, count how many times the success/failure
classification switches between consecutive entries; flaky when that count
is **greater than 1**. `Passed -> Failed -> Passed` (2 switches) is flaky;
`Passed -> Failed -> Failed` (1 switch — a regression, not flakiness) is
not; `Failed -> Passed` (1 switch — a recovery) is not. This is
intentionally a simple, deterministic rule — no statistical scoring.

Flakiness is **environment-aware**: the window above is taken only from the
TestCase's results on the *same Environment as its most recent execution*
(`TestHistoryService.BuildSummary`), using the run's immutable
`EnvironmentNameSnapshot` — never mixing Environments together when
detecting alternation. A Pass on the real target followed once by a Fail on
a different, controlled Environment (see
[Regression/Recovery demonstration](#regressionrecovery-demonstration)
below) must not make the real target's own consistent history look flaky,
and doesn't. `Regression`/`Recovery`/`StillPassing`/`StillFailing`/
`CurrentFailureSince` stay cross-environment and unaffected by this
scoping — only `IsFlaky` is Environment-scoped. See it working: open
`/tests` (or `/tests/2`'s history) — `test_search_is_partial_match` is
**not** flagged Flaky despite its real, different-Environment `Failed`
result at Run #4, because Roie's own history never alternates.

## Evidence (TM-08 / Step 6)

Same architecture the project chose from the start (Step 2): **SQLite
stores only `EvidenceArtifact` metadata + a relative path; the actual
binary/text content lives on disk** under `test-management/artifacts/`.
Reasons: keeps SQLite small, artifacts stay directly inspectable, and it's
simple to explain — the same rationale as choosing disk-based Playwright
reports over embedding them in a database.

**Directory layout**: `artifacts/run-{runId}/{sanitized-external-id}/...`
(e.g. `run-12/api_test_api02_status_codes.py_test_create_offender_returns_201/`).
A runner cannot know its own `ScenarioResultId` in advance (it only knows
its own stable `ExternalId`), so — per the documented fallback in the
plan — evidence is written into a safe, sanitized *stable-identity* folder
under the run's own root instead of a `scenario-{id}` folder; the
`EvidenceArtifact.ScenarioResultId` foreign key is what actually
disambiguates ownership, and a fresh `run-{runId}` root per run means the
same TestCase's evidence from two different runs can never collide,
overwrite, or be confused for each other's.

**Ownership & immutability**: every `EvidenceArtifact` row is `Add`-ed,
never updated — a later run's `artifact_created` event always creates a
brand-new row (and a brand-new file, under that run's own `run-{runId}/`
folder) pointing at a different `ScenarioResultId`; an older
`ScenarioResult`'s evidence is never touched by a later run. Verified
directly: `EvidenceTests.OlderRunEvidence_IsNotReplacedByALaterRunOfTheSameTestCase`,
and live (6.20) — see Step 6's verification section in `PART5_PLAN.md`.

**Runner protocol extension**: the same `OW_EVENT|{json}` stream from Step
4 gained one more event type, `artifact_created`, carrying only metadata —
`externalId`, `artifactType`, `path` (relative to the run's own artifact
directory), `contentType`. **No binary content is ever sent through
OW_EVENT or SignalR.** The orchestrator (`HandleArtifactCreatedAsync` in
`RunOrchestrator.cs`) never trusts a reported path blindly: it resolves the
path against this run's own artifact root, rejects anything that resolves
outside it (path traversal), rejects a path whose file doesn't actually
exist, and only then registers the `EvidenceArtifact` row (with the file's
*real*, backend-measured size — never a runner-reported size).

**Where each runner writes**: the orchestrator creates `artifacts/run-{id}/`
before launching pytest, and passes its absolute path to both suite
phases via `OFFENDERWATCH_ARTIFACT_DIR` (alongside `OFFENDERWATCH_BASE_URL`
from Step 4). If that variable is unset (running either suite by hand,
outside the platform), evidence capture is silently skipped — the suites
remain exactly as independently runnable as before.

- **pytest** (`automation/api/ow_event_reporter.py`): every scenario gets
  an `execution.log` (nodeid, status, duration, pytest's own captured
  stdout/log, and on failure the failure message + full traceback).
  `automation/api/evidence_capture.py` is a small module wired into the
  shared `session` fixture's `requests` response hook (`conftest.py`) — it
  observes every HTTP call the test makes without any test file changing,
  and the *last* request/response pair observed for a scenario (the one
  most likely relevant to a failure) is written as `api-request.json` /
  `api-response.json`. Sensitive headers (`Authorization`, `Cookie`,
  tokens/API keys) are redacted before anything is written.
- **Playwright** (`automation/ui/reporters/ow-event-reporter.js`): every
  scenario gets `execution.log` (steps, stdout/stderr, and on failure the
  failure message + stack). `playwright.config.js`'s `screenshot` setting
  changed from `'only-on-failure'` to **`'on'`** — Playwright's own
  built-in per-test screenshot now fires for *every* scenario, not only
  failures, satisfying "every UI scenario gets a final screenshot" with no
  test-file change at all; the reporter just copies that attachment (plus
  `trace.zip`, still `'retain-on-failure'`, unchanged) into the scenario's
  evidence folder. All of Playwright's own existing HTML/JSON reports and
  failure-screenshot behavior are unaffected — this is additive.

**Retrieval API**: `GET /api/runs/{runId}/scenarios/{scenarioResultId}/evidence`
lists metadata only (id, type, contentType, sizeBytes — never a filesystem
path). The browser fetches actual bytes from
`GET /api/evidence/{id}/content`, which re-resolves the artifact's relative
path against the configured artifact root *at request time* (not trusting
the ingestion-time validation alone — defense in depth), re-rejects path
traversal, re-verifies the file still exists, and returns it with its
recorded `Content-Type`; an unknown id or missing file is a plain 404, never
a stack trace or a raw path.

**UI**: an "Evidence" action on each finished scenario row on
`/runs/:id` opens a simple panel (not a full report viewer) — logs and API
request/response JSON render inline, screenshots render as `<img>`, a trace
is offered as a download link.

**Retention**: evidence is never auto-deleted. Deleting an Environment
(Step 3's `SET NULL` behavior) does not touch any Run's ScenarioResults or
their evidence. Cancelling a Run marks only still-Queued/Running scenarios
Cancelled (Step 4) and never removes evidence already registered for
scenarios that finished before the Stop.

## Test data lifecycle (TM-06 / Step 7)

**Ownership is explicit, never inferred.** A `TestDataRecord` exists only
because a real automation scenario's own confirmed creation was reported
through the `OW_EVENT` stream — never because a National ID happened to
start with `AUTO`, never by scanning the target app and guessing. `AUTO` is
retained purely as a *second, additional* safety check applied at cleanup
time (see Seed protection below), not as the ownership mechanism itself.

**Runner event**: one more `OW_EVENT` type, `test_data_created` — the
top-level `ExternalId` keeps its established meaning (the *creating
scenario's* stable identity, same as every other event); the created
target entity itself is described by three new fields kept deliberately
separate (`entityType`, `entityExternalId`, `entityIdentifier`) so the two
concepts never collide in the JSON. For an `Offender`, `entityExternalId`
is the real numeric id the target app returned; `entityIdentifier` is its
National ID. A `LocationPoint`'s create response carries no id at all
(verified live against the real API: `POST .../locations` returns just
`{"ok":true}`) — `entityExternalId` is correctly `null`, and
`entityIdentifier` carries only safe context (`offenderId=<id>`) for
inspection.

**How each suite detects creation, without touching test semantics:**
- **pytest** (`automation/api/test_data_capture.py`): a second, independent
  response hook on the same shared `session` fixture evidence capture
  already uses — it recognizes a successful `POST /api/offenders` or
  `POST /api/offenders/{id}/locations` purely from the target app's own
  response, and `ow_event_reporter.py`'s `_finish()` emits
  `test_data_created` for whatever it captured, regardless of the
  scenario's pass/fail status (a defect-confirming "should have been
  rejected but wasn't" failure is exactly the case that most needs
  tracking). No pytest test file was touched.
- **Playwright** (`automation/ui/reporters/test-data-capture.js`): a
  Reporter runs in a different OS process than the actual test code, so
  there is no shared in-memory hook to lean on the way pytest's `session`
  allows. `testInfo.attach()` is Playwright's own sanctioned worker→reporter
  channel; `registerOffenderCreated(testInfo, {...})` attaches a small JSON
  payload immediately after the target app's own response confirms
  creation, and `ow-event-reporter.js`'s `onTestEnd` reads it back and
  emits the same `test_data_created` contract. This required one additive
  call each in `fr03-create-validation.spec.js` and
  `fr10-location-validation.spec.js` — at their pre-existing
  "found real created items, about to clean them up" points — the minimal
  explicit-registration fallback the plan anticipates when centralized,
  test-file-free interception genuinely isn't available (unlike pytest's
  `session` hook). No assertion or test semantics changed.

**Attribution** (`RunOrchestrator.HandleTestDataCreatedAsync`): `TestRunId`
is always correct — the event physically arrived on *this run's own* piped
child-process stdout, nothing else could have produced it. `ScenarioResultId`
is attached "where available" and left `null` rather than guessed if the
reporting scenario can't be resolved for this run.

**Cleanup** (`TestDataService`) never scans or re-reads the current
Environment. The browser sends only a `TestDataRecord` id (or an explicit
list of ids — an empty list is a validation error, never "clean
everything"); the backend resolves the real target id and the target URL
entirely server-side from that row and its owning `TestRun.BaseUrlSnapshot`
— the immutable snapshot frozen at Run creation (Step 3), never a live
re-read of the Environment, so cleanup still targets the right place even
if that Environment was later edited or deleted (verified live — see
`PART5_PLAN.md`'s Step 7 section).

**Delete response mapping** — verified directly against the real target
API before assuming anything: `DELETE /api/offenders/{id}` returns **204**
on a genuine delete; a delete of an already-gone or unknown id reliably
returns **404** (this is the one *reliable* "gone" signal the app
provides — its `GET /api/offenders/{id}` is not reliable for this, since it
returns 200 for an unknown id too, the same behavior as known defect
BUG-012). So:
- 204 or 200 → `Cleaned`, `CleanedAtUtc` set (deleted now).
- 404 → `Cleaned`, `CleanedAtUtc` set (confirmed already gone — 7.12).
- anything else (5xx, other 4xx, timeout, connection failure) →
  `CleanupFailed`, never treated as "gone".

**LocationPoint cleanup is not supported, and this is by design, not an
oversight.** Inspecting the real target API (its own swagger contract, plus
a live, disposable-offender probe performed during this step) confirmed
two things: there is **no endpoint at all** to delete an individual
location point, and **deleting the parent Offender does not cascade-delete
its trail data either** — a location point created against offender 554
was still fully retrievable via `GET /api/offenders/554/trail` *after*
that offender was deleted (204) — this is a real, previously-undocumented
mechanism behind BUG-015 ("`totalLocationPoints` doesn't decrease after
deletion"): the trail rows are never actually removed by anything the API
exposes. `TestDataService` reflects this honestly — a `LocationPoint`
record's cleanup is always refused (`CleanupFailed`, with a clear reason
logged) *before* attempting any HTTP call, rather than fabricating a
success or calling an endpoint that doesn't exist. `LocationPoint` records
are still registered and shown for ownership/audit visibility (7.3
explicitly permits this for "inspection" purposes even without a real
cleanup path).

**Seed protection — defense in depth (7.9).** Two independent conditions
must *both* hold before any destructive call is made against an `Offender`:
(1) an explicit `TestDataRecord` row exists (the primary, and only real,
ownership mechanism), **and** (2) that row's own `Identifier` starts with
`AUTO` — a second, additional guard, checked only after ownership is
already established, never as a substitute for it. A record that fails
either check is refused with `CleanupFailed` and the destructive call is
never attempted — verified directly against real data during this step's
verification (a genuinely-owned record whose National ID happened not to
be `AUTO`-prefixed, from `test_create_offender_rejects_duplicate_national_id`'s
duplicate-id scenario, was correctly refused).

**Retry / already-cleaned**: `CleanupFailed` → `Cleaned` on a later retry
is a normal, supported transition. An already-`Cleaned` record is a no-op
on a repeat clean request — it never re-issues the DELETE call.

**History/evidence independence (7.19)**: cleaning a `TestDataRecord`
touches only the target application entity (or, when it fails safely,
nothing at all). It never deletes the `TestDataRecord` row itself (that
row is retained permanently as ownership/audit history — 7.11), and never
touches `TestRun`/`TestCase`/`ScenarioResult`/`EvidenceArtifact` rows or
files. Verified directly (`PART5_PLAN.md`'s Step 7 section) — a Run's full
scenario history and evidence were re-fetched and found unchanged after
cleaning every eligible record from that Run.

**`GET /api/test-data`** supports optional `status`/`entityType`/`runId`
query filters. **`POST /api/test-data/{id}/clean`** cleans one record.
**`POST /api/test-data/clean`** takes an explicit `{"ids": [...]}` list
(LocationPoints are always processed ahead of Offenders in a batch — 7.14 —
though this only matters for consistent ordering, since LocationPoint
cleanup itself is always refused); each id is processed independently, so
one failure never hides another's success.

**Legacy vs. platform cleanup**: `automation/api/cleanup_test_data.py`
remains exactly what it always was — a standalone developer/maintenance
convenience that finds every `AUTO`-prefixed offender on the live demo app
and deletes it, with **no ownership tracking, no Run/Scenario attribution,
and no relationship to SQLite at all**. It is not run automatically after
a Part 5 Run (doing so would defeat the point of TM-06 — there would be
nothing left in the Test Data page to demonstrate tracking or cleaning
through the platform). TM-06 is the real, tracked, auditable, run-scoped
mechanism; the legacy script stays only as a manual "tidy the shared demo
app" utility, unrelated to Part 5.

## Dynamic Dashboard (TM-07 / Step 8)

`GET /api/dashboard` (`Services/DashboardService.cs`) is the one
purpose-built, backend-derived release overview — the React client never
downloads raw Runs/ScenarioResults and recomputes the picture itself.
Everything below is aggregation over already-correct, already-tested data;
nothing here is a second implementation of a rule that already exists
elsewhere on the platform.

**Pass-rate formula** (used everywhere on the Dashboard, no exceptions):

```
Passed / (Passed + Failed + ExpectedFail) * 100
```

Skipped and Cancelled are excluded from the denominator. If the
denominator is zero, pass rate is `null` — never reported as 100%. The
three inputs are the exact same `PassedCount`/`FailedCount`/
`ExpectedFailedCount` totals `RunOrchestrator.FinalizeAsync` already
persists on every `TestRun` (Step 4) — Cancelled scenarios are already
excluded from all three there, so applying this formula to them is not a
second definition, just this one formula applied to the one existing
source of truth.

**Latest Run per Environment**: grouped by the immutable
`EnvironmentNameSnapshot` (never the live `Environment` row, which may
since have been edited or deleted — Step 3's historical-preservation
design), one row per group — its most recently *created* Run that reached
a terminal status (`Completed`/`Stopped`/`Failed`). A still-`Queued`/
`Running` Run is never picked as "latest" — it isn't a picture of anything
yet.

**Pass-rate trend**: the latest 20 (documented limit) Runs that produced
at least one comparable result (`Passed+Failed+ExpectedFail > 0`) —
excludes a Run that never started and a Stopped/infrastructure-`Failed`
Run that never got far enough to finish even one scenario; a `Stopped` Run
that *did* complete real scenarios before being cancelled is still real
data and is kept. Returned chronological (oldest first).

**Currently failing tests**: reuses `ITestHistoryService`'s own
`CurrentFailureSinceRunId` output (Step 6) directly — a TestCase is
"currently failing" exactly when that value is non-null, the identical
rule the `/tests` page already shows, never a second implementation.
Skipped/Cancelled results never hide an existing failure and never break a
CurrentFailureSince streak (Step 6's own rule, unchanged). A recovered
test (latest comparable result is `Passed`) disappears from this list
automatically. Failure duration (`GeneratedAtUtc - CurrentFailureSinceUtc`)
is computed on every read — never persisted, since it changes every second
it's true.

**Go / No-Go / Incomplete / No Data** — deterministic, based on the single
most recently *created* Run across the whole platform (not per
environment — one platform-wide "what does the newest attempt say"
signal):

| Latest Run's state | Decision |
|---|---|
| No Run exists at all | **NoData** |
| `Queued` / `Running` / `Stopped` | **Incomplete** |
| `Failed` (infrastructure) | **NoGo** |
| `Completed`, `FailedCount > 0` | **NoGo** |
| `Completed`, `FailedCount == 0` | **Go** |

`ExpectedFail` never forces `NoGo` by itself — it represents a known,
already-classified defect, not a regression — but expected-failure counts
stay prominently visible everywhere alongside the decision, never folded
into the unexpected-failure count. A `Stopped` Run is explicitly never
presented as a successful `Go` — it's `Incomplete`.

**Client**: `pages/DashboardPage.tsx` renders the decision banner, the
Latest-Run-per-Environment table (links to `/runs/:id`), a lightweight
dependency-free SVG trend chart (`components/PassRateTrendChart.tsx` — no
charting library), and the Currently-Failing-Tests table (links to
`/tests/:id` and to the failure's origin Run) — all from one
`GET /api/dashboard` call. A manual **Refresh** button reloads it; no
polling, no SignalR (both intentionally out of scope for TM-07 per the
plan). Loading/error states match the rest of the app (an error banner
with Retry replaces the page entirely on API failure — the old placeholder
is never left visible).

**Part 4 relationship**: `dashboard/dashboard.html` (the static Part 4
deliverable) is untouched and remains the submitted Part 4 artifact. The
Part 5 React Dashboard is a separate, independent implementation that
functionally replaces it *for the new platform only* — it reads live
SQLite data through the Part 5 API, never the Part 4 dashboard's own
data/calculations.

## Regression/Recovery demonstration

Nearly every defect in this app is **deterministic** (the same input
always produces the same wrong output) — which is itself the honest
finding from Part 3, not a shortcoming of this platform: real, identical
runs against the real demo app produce byte-for-byte identical results
every time. To demonstrate a real Regression → Recovery transition
**without changing any assertion or touching any persisted row directly**,
a second, deliberately controlled Environment was registered through the
platform's own `/environments` page — a temporary local HTTP server
(`http://127.0.0.1:8792`, stdlib `http.server`, not part of any
deliverable) standing in as a real, different, *legitimate* execution
target, exactly the "legitimate scenario/environment/setup" the plan
anticipates for an app whose defects don't naturally flap. It answers `GET
/api/offenders` with an empty result set, which makes
`test_search_is_partial_match` (normally `Passed` against the real app)
genuinely fail its own real assertion ("no offender with a usable last
name found").

This is exactly how the final submission's Regression/Recovery pair was
produced (see [Final submission data](#final-submission-data) below) — the
technique is not a one-off; it's documented, reusable, and named clearly
enough (`Local Regression Demo Target (not the real app)`) that no
reviewer mistakes it for a second real target environment. After it had
served its purpose the Environment *row* was deleted through the real
`DELETE /api/environments/{id}` endpoint — its Run's `EnvironmentNameSnapshot`/
`BaseUrlSnapshot` survive intact regardless (Step 3's historical-preservation
design), which is exactly why `/environments` shows one clean, real,
currently-usable Environment while the Dashboard/Test History still show
two Environments' worth of real historical data.

## Bonus features

Attempted bonuses, per the assignment's bonus list:

### B-01 — Flakiness detection ✅ implemented

Covered above under [Test history](#test-history-tm-04--step-6) — a
per-Environment last-10-comparable-results switch-count rule
(`HistoryClassifier.ComputeIsFlaky`, scoped per-Environment in
`TestHistoryService.BuildSummary`). **See it working**: open `/tests` —
the *Flaky* column/badge; `/tests/2` for the specific case where a
real, different-Environment failure does **not** make the real target's
own consistent Pass history look flaky.

### B-02 — Run comparison ✅ implemented

A read-only diff between any two existing Runs — the view a QA lead would
open before approving a release.

- **API**: `GET /api/runs/compare?baseRunId={id}&compareRunId={id}`
  (`Controllers/RunController.cs` / `Services/RunComparisonService.cs`).
  Direction is explicit and always **Base Run -> Compare Run**. Built
  entirely on top of the existing `IRunService.GetByIdAsync` (the same data
  `/runs/:id` already renders) — no new table, nothing invented, and
  scenarios are matched between the two runs by the stable `TestCase.Id`
  (via `ScenarioResultDto.TestCaseId`), never by display name.
- **Classification** (`Services/RunComparisonClassifier.cs`, pure and
  independently unit-tested — a deliberately separate class from
  `HistoryClassifier`, so B-02 can never change TM-04's own history
  behavior): `Regression` (Base `Passed` -> Compare `Failed`), `Recovery`
  (Base `Failed`/`ExpectedFail` -> Compare `Passed`), `New` (only in
  Compare), `Missing` (only in Base), plus truthful `StillPassing` /
  `StillFailing` / `ExpectedFailure` (both sides `ExpectedFail` — a known
  defect staying known) / `OtherChange` (e.g. `Passed -> ExpectedFail`,
  deliberately **not** an automatic unexpected Regression, per the
  assignment) / `Unchanged`. A Skipped/Cancelled result on either side is
  never comparable — it is reported as `Unchanged` (identical) or
  `OtherChange` (different), and never manufactures a false
  Regression/Recovery.
- **Different Environments are allowed, never blocked** — comparing across
  Environments is exactly what "any two runs" requires. When the two runs'
  immutable `EnvironmentNameSnapshot`/`BaseUrlSnapshot` differ, the API sets
  `environmentsDiffer: true` and the UI shows a prominent warning banner
  ("These runs were executed against different environments..."). The
  comparison always uses each run's own frozen snapshot, never a live
  Environment lookup — it still works correctly even if that Environment
  was since renamed or deleted.
- **Incomplete runs**: if either run's status isn't `Completed`
  (`Queued`/`Running`/`Stopped`/`Failed`), the API sets
  `baseRunIncomplete`/`compareRunIncomplete` and the UI shows a warning that
  the comparison may not represent a complete suite — the comparison itself
  is never hidden or blocked.
- **UI**: `/runs/compare` (also `/runs/compare?base={id}&compare={id}` for a
  direct/shareable link), reached via the **Compare Runs** link on the
  `/runs` page. Two app-styled Run selectors (Base/Compare, each showing
  Run #, Environment, date/time, status), a Compare button that's disabled
  when the same run is picked on both sides, then: a Base -> Compare
  summary with per-run Environment/status/trigger/timing, four summary
  cards (Regressions/Recoveries/New/Missing), a Totals-delta table
  (Passed/Failed/ExpectedFail/Skipped/Total, each Base -> Compare and the
  change), a filterable Test Differences table (All Changes / Regressions /
  Recoveries / New / Missing / Unchanged) using the existing status-badge
  styling, and a click-through on any test row into its existing
  `/tests/:id` history view (TM-04) — B-02 never duplicates that
  implementation. Selecting/changing runs here never creates, starts, or
  modifies any Run.
- **See it working**: open `/runs`, click **Compare Runs**, pick Run #4
  (`Local Regression Demo Target`) as Base and Run #5 (`Roie (Live Demo)`)
  as Compare — 5 Recoveries, the different-environments warning banner, and
  the full per-test diff (this is the same Run #4/#5 pair used for the
  [Regression/Recovery demonstration](#regressionrecovery-demonstration)
  above).
- **Tests**: `server.Tests/RunComparisonServiceTests.cs` — Regression/
  Recovery/New/Missing classification, non-regression for
  Passed->Passed/Failed->Failed, ExpectedFail handling (both directions),
  Skipped/Cancelled never producing a false transition, totals-delta
  correctness, cross-Environment comparison + the `environmentsDiffer`
  flag, immutable-snapshot reuse (no live Environment row needed),
  nonexistent-run 404, same-run-twice validation, Stopped/incomplete-run
  warning flags, empty-run comparison, and a read-only guarantee (neither
  Run/ScenarioResult is modified by comparing).

### B-05 — One-command startup ✅ implemented

`docker compose up --build` from the **repo root** brings up the whole
platform — API + client + SQLite persistence — with only Git/Docker/Docker
Compose installed on the machine (no local .NET SDK/Node/Python/Playwright
required for this path).

- **Prerequisite**: Docker + Docker Compose (Docker Desktop on
  Windows/macOS, or the `docker compose` plugin on Linux).
- **Command** (from the repo root, the same directory as this README's
  parent's parent — where `docker-compose.yml` lives):
  ```
  docker compose up --build
  ```
- **Open the UI**: <http://localhost:8081> — the full React app, served by
  nginx.
- **Swagger/API directly**: <http://localhost:5174/swagger> (same port
  local `dotnet run` already uses) — also reachable same-origin through the
  UI's own origin at <http://localhost:8081/swagger>.
- **Stop**: `docker compose down` — stops and removes the containers only.
  **Never run `docker compose down -v`** as routine — there is no Docker
  volume to begin with (see persistence below), so `-v` has nothing to do
  here, but the flag exists specifically to destroy persistent volumes and
  must never become a habit.
- **Architecture**: two containers, no database container — SQLite stays
  SQLite. `server` (`test-management/server/Dockerfile`, multi-stage:
  `dotnet publish` on the SDK image, runs on the ASP.NET runtime image) and
  `client` (`test-management/client/Dockerfile`, multi-stage: `npm ci &&
  npm run build`, served by nginx — never the Vite dev server in Docker).
  Same-origin routing: the browser only ever talks to nginx
  (`test-management/client/nginx.conf`), which reverse-proxies `/api/` and
  `/hubs/` (SignalR, with `Upgrade`/`Connection` headers for the WebSocket
  transport TM-03 needs) and `/swagger/` to the `server` container over
  Docker's internal network — the browser itself never needs to resolve a
  Docker service name.
- **SQLite persistence**: `test-management/data/` and
  `test-management/artifacts/` are **bind-mounted** straight from the host
  (`docker-compose.yml`'s `volumes:`) into the container at the exact same
  relative paths the app already resolves locally — not copied into an
  image layer, and not a separate named Docker volume either. This means
  `docker compose up` on a freshly-cloned repo shows the platform's real,
  already-committed historical data immediately, and a restart
  (`docker compose down && docker compose up`) can't lose anything — the
  data was never inside a container to begin with. Verified locally:
  identical Run/ScenarioResult/TestDataRecord counts, and the same evidence
  file byte-readable, before and after a full `down`/`up` cycle.
- **pytest in Docker**: the `server` image installs Python 3 + the repo's
  own pinned `automation/api/requirements.txt` (`requests==2.32.3`,
  `pytest==8.3.5`, `pytest-html==4.1.1`) and copies `automation/api/` in
  verbatim — RunOrchestrator's `python3 -m pytest -v` child-process launch
  is completely unchanged.
- **Playwright in Docker**: the image installs Node 20 + `npm ci` against
  the repo's own pinned `automation/ui/package-lock.json`
  (`@playwright/test@1.62.1`) and runs `npx playwright install --with-deps
  chromium` so the Chromium binary + its Linux OS libraries are present —
  no host browser install to fall back on. The Windows-only
  `node_modules/.bin/playwright.cmd` default remains the local default;
  `appsettings.Docker.json` (loaded only when
  `ASPNETCORE_ENVIRONMENT=Docker`, set by the Dockerfile) overrides just
  `Runner:PlaywrightExecutableRelativePath` to the Linux
  `node_modules/.bin/playwright` shim and `Runner:PythonExecutable` to
  `python3` — a config override, not an OS-branch in `RunOrchestrator`
  itself, so Windows local development is completely untouched.
- **Windows local development remains available and unaffected**: `dotnet
  run` (backend) and `npm run dev` (frontend) still work exactly as before
  — nothing about local `appsettings.Development.json`, ports, or
  `RunnerOptions` defaults changed; Docker only adds an additional,
  optional `appsettings.Docker.json` layer that local runs never load.
- **Health checks**: the `server` container has a Docker `HEALTHCHECK`
  against `/api/health`; `client` `depends_on: server: condition:
  service_healthy`, so nginx only starts once the API is actually ready —
  a slow API start never shows the reviewer a "502" on first load.
- **Target Environments stay dynamic**: nothing OffenderWatch-specific
  (`Roie`, `Base Application`, or any BaseUrl) is hard-coded anywhere in
  Docker config — every Run still targets whatever Environment is selected
  through the platform itself (TM-01), completely unchanged.
- **Verified**: `docker compose config`, `docker compose build`, and
  `docker compose up` all succeed; the UI, REST API, Swagger, and SignalR
  negotiation (WebSockets listed as the first available transport) all work
  through the containerized reverse proxy; all of `/runs`, `/runs/:id`,
  `/tests`, `/test-data`, `/runs/compare`, and evidence content load real
  historical data identical to local `dotnet run`; SQLite + evidence
  persistence survive a full container recreation. **Not** verified inside
  Docker: actually starting a Run (would make a real, non-reversible call
  against the live OffenderWatch target — Python/pytest/Node/Playwright
  toolchain presence and versions inside the container were confirmed
  directly instead, `python3 -m pytest --version` / `npx playwright
  --version`, matching the repo's pinned versions exactly).

### B-06 — Platform self-tests ✅ implemented

`server.Tests/` covers the platform API itself (136 tests, `dotnet test`),
including — per the bonus's own minimum bar — both named areas in depth:
TM-04 history/transition logic (`HistoryClassifierTests.cs`,
`TestHistoryServiceTests.cs`: transitions, CurrentFailureSince, LastPass,
environment-aware flakiness) and TM-06 test-data cleanup's seeded-data
protection (`TestDataServiceTests.cs`:
`Clean_SeedSafetyGuard_RejectsRecordWhoseIdentifierIsNotAutoPrefixed`, the
LocationPoint-unsupported-cleanup guard, and more) — plus RunOrchestrator
event ingestion/idempotency, RunService, EnvironmentService, the Dashboard
service, evidence, and B-02's own comparison logic above.

## Final submission data

`test-management/data/testmanagement.db` (committed) and
`test-management/artifacts/` (committed) together are the final submission
dataset — everything in both was produced by real executions through the
actual Part 5 application and runner flow. Nothing was inserted, edited,
or faked directly in SQLite.

- **6 real recorded Runs** (`Run #1`–`#6`), all `Completed` except `Run #3`
  (deliberately `Stopped` mid-execution, live, from the React UI — a real
  demonstration of TM-02's cancellation behavior: its already-finished
  scenarios stayed final, its not-yet-started ones became `Cancelled`).
- **2 Environments represented** in the historical Run data —
  `Roie (Live Demo)` (the real target, still configured and usable) and
  `Local Regression Demo Target (not the real app)` (the controlled
  Environment described above, whose Environment row was deleted after
  producing the demonstration — its Run's snapshot remains, exactly
  proving TM-01's own deletion-safety guarantee).
- **A real Regression → Recovery pair**, `test_api01_paging_search.py::test_search_is_partial_match`:
  `Run #1 Passed (FirstResult)` → `Run #2/#3 StillPassing` →
  `Run #4 Failed (Regression, against the controlled Environment)` →
  `Run #5 Passed (Recovery, back against the real app)` — visible on
  `/tests/2` and cross-verified against `GET /api/tests/2/history`.
- **`ExpectedFail` examples throughout** — 26 known, `BugId`-tagged
  defects reproduce identically on every real run against the real app.
- **Historical UI screenshot evidence** — every UI scenario across all 6
  runs has a final screenshot (pass or fail); failed/`ExpectedFail` ones
  also have a Playwright trace.
- **Historical API request/response evidence** — every API scenario across
  all 6 runs has its final captured request/response JSON pair.
- **`TestDataRecord` lifecycle history** — all three states present:
  `Cleaned` (real `AUTO`-owned Offenders, deleted through the real target
  API via the React Test Data page, including one clicked individually
  live and the rest through **Clean All Active**), `CleanupFailed`
  (`LocationPoint` records, refused by design — see [Test data
  lifecycle](#test-data-lifecycle-tm-06--step-7) — and one genuinely-owned
  Offender whose National ID wasn't `AUTO`-prefixed, correctly refused by
  the seed-safety guard), and the underlying rows are retained permanently
  either way.
- **Useful Dashboard data** — with `Run #5`/`#6` (both real, both clean)
  as the latest, the Dashboard shows `Go`, a 6-point pass-rate trend, and
  the 26 durably-`ExpectedFail` tests as "currently failing" (accurate —
  they are still failing, just as a known, classified defect, not a
  regression).

**Evidence immutability was re-verified for this submission specifically**:
one of `Run #1`'s screenshots was hashed (MD5), five more real runs then
executed (`Run #2`–`#6`, including the Regression/Recovery pair and a
live Stop), and the same artifact was re-fetched and re-hashed —
byte-for-byte identical — then reopened successfully through the React UI.
Original seeded OffenderWatch data (the 11 seed offenders and their
trails) was confirmed unaffected before and after every real run and
every cleanup performed for this submission.

## Interview demo flow

A suggested walkthrough of the live application (all of it was actually
exercised, live, against real backend/API data, while preparing this
submission):

1. **Dashboard** (`/`) — the release decision banner, latest-run summary,
   pass-rate trend, currently-failing tests.
2. **Environments** (`/environments`) — add/edit/delete, the single-default
   invariant.
3. **Start a Run** (`/runs` → Start New Run) — creates and enqueues a Run,
   returns immediately.
4. **Watch it live** — scenarios transition `Queued → Running → final
   status` on `/runs/:id` via SignalR, with zero manual refresh; the
   connection indicator shows `Live`.
5. **Stop a controlled Run** — click Stop mid-execution; the status flips
   to `Stopped` live, completed scenarios stay final, the rest become
   `Cancelled`.
6. **Open Tests** (`/tests`) — every tracked TestCase, last status,
   flaky indicator.
7. **Show Regression/Recovery history** (`/tests/2`) — the real
   transition sequence described above.
8. **Open an older `ExpectedFail`/`Failed` scenario** from an early Run's
   detail page.
9. **Open its evidence** — screenshot/log for a UI scenario, or
   request/response JSON for an API scenario.
10. **Open Test Data** (`/test-data`) — the full explicit-ownership
    lifecycle: `Active`/`Cleaned`/`CleanupFailed`.
11. **Perform one safe owned cleanup** — click Clean on an `Active`,
    `AUTO`-prefixed Offender row; watch it become `Cleaned` from the real
    backend response.
12. **Verify retention** — return to that record's owning Run; its
    scenario history and evidence are untouched.
13. **Run Comparison** (`/runs/compare`) — pick Run #4 (Base) → Run #5
    (Compare): Regressions/Recoveries/New/Missing summary cards, the
    totals delta, the different-environments warning, and the per-test
    diff table.
14. **Return to Dashboard** — confirm it still reads correctly after
    everything above.
15. **Mention Docker** — `docker compose up --build` from the repo root
    brings up the same platform (API + client + SQLite) in two containers;
    and mention the implemented bonuses: **B-01** (environment-aware
    flakiness), **B-02** (Run Comparison, just shown), **B-05**
    (one-command Docker startup), **B-06** (136 backend tests covering
    TM-04/TM-06 and more) — see [Bonus features](#bonus-features) above.

## Design tradeoffs

A short, interview-ready list of the deliberate simplifications made and
why, beyond what's already explained inline near each feature above:

- **Sequential run execution, one worker.** pytest then Playwright, one
  Run at a time, no concurrency. Simpler process ownership, simpler
  cancellation, lower load on the shared demo app, and the assignment
  explicitly doesn't require concurrent runs.
- **Evidence on disk, metadata in SQLite** (not BLOBs) — keeps the
  database small and evidence directly inspectable with any file browser;
  the tradeoff is that the database and the `artifacts/` folder must be
  shipped/restored together (both are committed for this reason).
- **`LocationPoint` cleanup is refused, not faked.** The real target API
  has no endpoint to delete one, and deleting the parent Offender doesn't
  cascade to it either (a real defect this project discovered — see [Test
  data lifecycle](#test-data-lifecycle-tm-06--step-7)). Rather than
  silently no-op or call an endpoint that doesn't exist, the platform is
  honest about the limitation.
- **The overall Go/No-Go decision looks at the single most recent Run
  platform-wide**, not per-Environment — the assignment's own wording
  doesn't fully disambiguate multi-Environment aggregation for the one
  top-level signal, and this is the simplest defensible reading (documented
  explicitly in `PART5_PLAN.md`'s Step 8 section).
- **No authentication, no scheduled runs, no run comparison, no
  notifications** — all explicitly out of scope per the assignment's own
  bonus/scope-boundary sections; the platform is deliberately not padded
  with unrequested features.
- **A lightweight hand-rolled SVG chart, not a charting library** — the
  trend visualization is a handful of `<svg>` elements; adding a
  dependency for one line chart wasn't justified.
