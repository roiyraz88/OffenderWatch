# OffenderWatch — Test Management Platform (Part 5)

Status: **Step 5 — Real-Time Execution** (TM-03) done.
See [`PART5_PLAN.md`](../PART5_PLAN.md) at the repo root for the full
implementation plan and current step.

TM-01 (Environment configuration), TM-02 (real run execution), and TM-03
(live SignalR progress) are fully working end-to-end. History calculations
(TM-04), evidence viewing (TM-08), and test-data cleanup (TM-06) are not
implemented yet — see `PART5_PLAN.md`'s Step 6–8 sections.

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
section; the evidence-storage model will be documented here once Step 6
implements it.
