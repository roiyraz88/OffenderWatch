# OffenderWatch — Test Management Platform (Part 5)

Status: **Step 6 — Test History & Evidence** (TM-04 + TM-08) done.
See [`PART5_PLAN.md`](../PART5_PLAN.md) at the repo root for the full
implementation plan and current step.

TM-01 (Environment configuration), TM-02 (real run execution), TM-03 (live
SignalR progress), TM-04 (test history/regression/recovery/flakiness), and
TM-08 (evidence capture/retrieval) are fully working end-to-end. Test-data
cleanup (TM-06) and the dynamic dashboard (TM-07) are not implemented yet —
see `PART5_PLAN.md`'s Step 7–8 sections.

## Structure

- `server/` — ASP.NET Core Web API (.NET 8, C#)
- `server.Tests/` — xUnit backend tests (temp-SQLite-per-test, never touch
  `data/testmanagement.db` or the real OffenderWatch app)
- `client/` — React + Vite + TypeScript
- `data/` — the SQLite database file
- `artifacts/` — evidence files (screenshots, logs) once Step 6 adds capture

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

## Run locally

**Server** (Swagger at `/swagger`, health check at `/api/health`):

```bash
cd server
dotnet run
```

Listens on `http://localhost:5174` by default (see
`server/Properties/launchSettings.json`). On first run (or after a schema
change), apply migrations first: `dotnet ef database update`.

**Client**:

```bash
cd client
npm install
cp .env.example .env.local   # points VITE_API_BASE_URL at the server above
npm run dev
```

Listens on `http://localhost:5173` by default — matches the server's
`ClientOrigins` CORS config in `server/appsettings.json`.

**Backend tests** (fast, deterministic, never touch the real OffenderWatch
app — see `server.Tests/`):

```bash
cd server.Tests
dotnet test
```

**Using the platform**: open `http://localhost:5173/environments`, add an
Environment pointing at a real OffenderWatch instance (e.g.
`https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie`), then go to
`/runs` and click **Start New Run**. You'll land on the run's detail page;
click **Refresh** to see progress (Step 4 has no live updates yet — see
above), or **Stop** while it's Queued/Running.

Full database schema rationale is documented in `PART5_PLAN.md`'s Step 2
section.

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

**Flakiness** (`IsFlaky`): among the **last 10 comparable** (non-neutral)
results, count how many times the success/failure classification switches
between consecutive entries; flaky when that count is **greater than 1**.
`Passed -> Failed -> Passed` (2 switches) is flaky; `Passed -> Failed ->
Failed` (1 switch — a regression, not flakiness) is not;
`Failed -> Passed` (1 switch — a recovery) is not. This is intentionally a
simple, deterministic rule — no statistical scoring.

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

## Regression/Recovery demonstration (6.27)

Nearly every defect in this app is **deterministic** (the same input always
produces the same wrong output) — which is itself the honest finding from
Part 3, not a shortcoming of this platform. Two full real runs back-to-back
against the real demo app (recorded during this step's verification)
produced byte-for-byte identical results, confirming this. To demonstrate
Regression/Recovery **without changing any assertion or touching any
persisted row directly**, a third real Environment was registered through
the platform's own `/environments` page pointing at a temporary local HTTP
stub (not the real demo app) that legitimately makes
`test_search_is_partial_match` fail its own real assertion ("no offender
with a usable last name found") by returning an empty offender list — a
real, different, real execution target, exactly the "legitimate
scenario/environment/setup" the plan anticipates for this situation.
Sequence actually recorded: Run 1 (real app) Passed -> Run 2 (real app)
StillPassing -> Run 3 (local stub) **Regression** -> Run 4 (real app)
**Recovery** — verified both via `GET /api/tests/{id}/history` and live in
the React Test Details page. The stub server, its Environment row, and its
Run were part of this verification only, not a permanent fixture.
