# OffenderWatch — Part 5 Implementation Plan

## 1. Purpose

This document defines the implementation plan for Part 5 of the
OffenderWatch QA Assignment.

The official assignment remains the source of truth for requirements.
This document defines the chosen architecture and incremental
implementation approach.

The existing Parts 1–4 must remain intact.

---

# 2. Existing Repository

Existing deliverables:

- `automation/ui`
  - Playwright
  - JavaScript
  - Existing UI automation suite

- `automation/api`
  - pytest
  - Python
  - requests
  - Existing API automation suite

- `dashboard`
  - Static Part 4 QA dashboard

These must not be rewritten or replaced unless a Part 5 integration
requirement makes a minimal change necessary.

The Part 3 automation suites remain the real test suites executed by
Part 5.

Known defects represented by `[BUG-xxx]` tests must preserve their
meaning.

---

# 3. Part 5 Technology Stack

Backend:
- ASP.NET Core Web API
- .NET 8+
- C#

Frontend:
- React
- Vite
- TypeScript

Database:
- SQLite
- Entity Framework Core

Real-time:
- SignalR

Automation:
- Existing Playwright and pytest suites

---

# 4. Architecture

Chosen integration model: ORCHESTRATED.

High-level flow:

React
  |
  v
ASP.NET Core API
  |
  +---- SQLite
  |
  +---- Test Run Orchestrator
           |
           +---- Playwright
           |
           +---- pytest
  |
  +---- SignalR
           |
           v
         React

The ASP.NET Core API will own the run lifecycle.

It will eventually:

1. Create a run.
2. Select its target environment.
3. Launch the existing automation.
4. Receive/parse scenario progress.
5. Persist scenario results.
6. Broadcast progress through SignalR.
7. Store evidence.
8. Support cancellation.
9. Finalize the run.

No mock test results may be used.

---

# 5. Part 4 Dashboard Relationship

`dashboard/dashboard.html` is the preserved static Part 4 deliverable.

It must not be deleted.

The new React Dashboard will eventually implement TM-07 and replace
the static dashboard as the live Go / No-Go source.

The React dashboard must use data from the Part 5 API / SQLite.

Do not copy hard-coded statistics from the old dashboard.

---

# 6. Planned Part 5 Structure

test-management/
├── server/
│   ├── Controllers/
│   ├── Data/
│   ├── Models/
│   ├── DTOs/
│   ├── Services/
│   ├── Hubs/
│   └── Migrations/
│
├── client/
│   └── src/
│       ├── api/
│       ├── components/
│       ├── pages/
│       ├── hooks/
│       └── types/
│
├── data/
├── artifacts/
└── README.md

---

# 7. Requirement Mapping

| Requirement | Description | Status |
|---|---|---|
| TM-01 | Environment configuration | DONE |
| TM-02 | Run execution & management | Planned |
| TM-03 | Real-time progress | Planned |
| TM-04 | Test history | Planned |
| TM-05 | Persistence | Planned |
| TM-06 | Test data lifecycle | Planned |
| TM-07 | Summary dashboard | Planned |
| TM-08 | Evidence capture | Planned |

Status must only be changed when the requirement actually works.

---

# 8. Implementation Phases

## Step 1 — Foundation

**STATUS: DONE (2026-08-31).**

Created:

- ASP.NET Core server (`test-management/server`, `dotnet new webapi
  --use-controllers`, .NET 8) with the planned folder layout (`Controllers/`,
  `Data/`, `Models/`, `DTOs/`, `Services/`, `Hubs/`, `Migrations/` — the
  latter five are empty placeholders (`.gitkeep`), populated starting Step 2)
- React/Vite/TypeScript client (`test-management/client`,
  `npm create vite@latest -- --template react-ts`) with the planned folder
  layout (`api/`, `components/`, `pages/`, `hooks/`, `types/` — `components/`
  and `types/` are still empty placeholders)
- EF Core SQLite dependencies: `Microsoft.EntityFrameworkCore.Sqlite` and
  `Microsoft.EntityFrameworkCore.Design` (8.0.10) added to the server
  `.csproj`. No `DbContext` yet — that's Step 2. Connection string
  placeholder added to `appsettings.json` pointing at
  `test-management/data/testmanagement.db`.
- SignalR dependencies: `builder.Services.AddSignalR()` registered in
  `Program.cs`; `@microsoft/signalr` added to the client. No hub class or
  mapped endpoint yet — that's Step 5.
- Swagger: enabled via the webapi template (`Swashbuckle.AspNetCore`),
  verified serving at `/swagger`.
- CORS: a `ClientApp` policy allowing the origins listed in
  `appsettings.json`'s `ClientOrigins` (defaults to
  `http://localhost:5173`, the Vite dev server) — no hard-coded origin in
  code.
- `GET /api/health` — `HealthController`, returns
  `{ status, service, timestampUtc }`. Verified locally (200 OK).
- Basic React application shell: `App.tsx` renders a header with nav links
  and a live `API: ok/checking/error` indicator (`useHealth` hook calling
  `GET /api/health` through `src/api/client.ts`, whose base URL comes from
  `VITE_API_BASE_URL`, not a hard-coded string).
- Placeholder routes, each a stub page component with a "Planned"-style note
  naming the step that will implement it:
  - `/` → `DashboardPage` (TM-07, Step 8)
  - `/runs` → `RunsPage` (TM-02, Step 4)
  - `/tests` → `TestsPage` (TM-04, Step 6)
  - `/environments` → `EnvironmentsPage` (TM-01, Step 3)
  - `/test-data` → `TestDataPage` (TM-06, Step 7)

Verified:

- `dotnet build` in `test-management/server` — 0 warnings, 0 errors.
- `npm run build` (`tsc -b && vite build`) in `test-management/client` —
  clean, no type errors.
- Runtime smoke test: `dotnet run` (server, `http://localhost:5174`) +
  `npm run dev` (client, `http://localhost:5173`) both started; `GET
  /api/health` returned 200 and `/swagger/index.html` returned 200.
- `.gitignore` updated at the repo root for `test-management/server/bin`,
  `.../obj`, `test-management/client/node_modules`, `.../dist`, and
  `.../.env.local` — the eventual SQLite DB file under
  `test-management/data/` is deliberately **not** ignored, since deliverable
  #6 requires committing it with recorded runs.
- Environment note: only .NET 6 SDK was present on this machine; installed
  .NET 8 SDK via `winget install Microsoft.DotNet.SDK.8` (with the user's
  confirmation) since the assignment fixes the stack at .NET 8+.

No business functionality implemented (by design — that starts Step 2).

STOP after Step 1. Awaiting review before Step 2 (Domain Model &
Database).

---

## Step 2 — Domain Model & Database

**STATUS: DONE (2026-08-31).**

Implemented exactly the schema below (2.1–2.11) with no deviation, plus one
naming fix not anticipated by the spec: `Environment` collides with
`System.Environment` under `ImplicitUsings` — resolved with a `using
Environment = OffenderWatch.TestManagement.Server.Models.Environment;`
alias in `Data/TestManagementDbContext.cs` rather than renaming the entity
(the spec names it "Environment"; `Models/TestRun.cs` itself needed no
alias since same-namespace resolution already prefers the sibling type).

Files added:

- `Models/Enums.cs` — `RunStatus`, `RunTrigger` (`Manual`/`Api` — no
  `Scheduled` value, per spec), `TestSuite`, `ScenarioStatus` (includes
  `ExpectedFail` distinct from `Failed`), `EvidenceType`,
  `TestDataEntityType`, `TestDataCleanupStatus`.
- `Models/Environment.cs`, `TestRun.cs`, `TestCase.cs`,
  `ScenarioResult.cs`, `EvidenceArtifact.cs`, `TestDataRecord.cs` — exactly
  the fields listed in 2.1–2.6, all navigation properties as described.
- `Data/TestManagementDbContext.cs` — `DbSet`s for all six entities;
  `OnModelCreating` configures (see 2.7):
  - `Environment.Name` unique index.
  - `TestCase.ExternalId` unique index.
  - `ScenarioResult(TestRunId, TestCaseId)` unique index.
  - `Environment -> TestRun`: `EnvironmentId` nullable FK,
    `DeleteBehavior.SetNull` (deleting an Environment never removes
    historical TestRuns; the snapshot fields keep the record meaningful
    regardless).
  - `TestRun -> ScenarioResult` / `TestRun -> TestDataRecord`:
    `DeleteBehavior.Restrict` (a TestRun is not expected to be deleted in
    normal operation; Restrict fails loudly instead of silently losing
    history if that's ever attempted).
  - `TestCase -> ScenarioResult`: `DeleteBehavior.Restrict` (same
    reasoning — a TestCase must keep existing for as long as any
    ScenarioResult references it).
  - `ScenarioResult -> EvidenceArtifact`: `DeleteBehavior.Cascade`
    (evidence only exists in the context of its ScenarioResult).
  - `ScenarioResult -> TestDataRecord` (the optional relationship):
    `DeleteBehavior.SetNull`.
  - All enum properties stored as strings (`HasConversion<string>()`) so
    the raw SQLite file stays directly readable without a lookup table.
  - Sensible `HasMaxLength` on every bounded string column;
    `FailureMessage`/`StackTrace` left unbounded (large free text).
  - Two extra non-unique indexes on `TestDataRecord` —
    `(TestRunId, CleanupStatus)` and `(EntityType, ExternalId)` — anticipating
    the Step 7 cleanup queries ("everything this run created that's still
    Active" / "is this app entity already tracked").
- `Program.cs` — registered `AddDbContext<TestManagementDbContext>`. The
  connection-string *pattern* still comes from `appsettings.json`
  (`ConnectionStrings:Default`, unchanged since Step 1:
  `Data Source=../data/testmanagement.db`); what's resolved in code is
  making a relative `DataSource` absolute against
  `builder.Environment.ContentRootPath` (via `SqliteConnectionStringBuilder`)
  instead of trusting the process's current working directory, and
  ensuring the target directory exists.
- `Migrations/20260831143752_InitialTestManagementSchema.*` +
  `TestManagementDbContextModelSnapshot.cs` — generated via
  `dotnet ef migrations add InitialTestManagementSchema` (installed
  `dotnet-ef` 8.0.10 as a global tool; wasn't present before this step).

No Controllers, DTOs, Services, or Hubs were added — those directories are
still empty placeholders. No React changes.

Verified:

- `dotnet build` (server) — 0 warnings, 0 errors.
- `dotnet ef database update` against a fresh (deleted-then-recreated)
  `test-management/data/testmanagement.db` — applied cleanly, single
  migration recorded in `__EFMigrationsHistory`.
- Inspected the resulting schema directly (`PRAGMA table_info` /
  `foreign_key_list` / `index_list` via Python's `sqlite3` module, no CLI
  installed): all 6 tables present with exactly the columns above; all
  three required unique indexes present
  (`IX_Environments_Name`, `IX_TestCases_ExternalId`,
  `IX_ScenarioResults_TestRunId_TestCaseId`); all FK delete behaviors
  match the list above (`SET NULL` / `RESTRICT` / `CASCADE` as designed —
  confirmed literally, not just by reading the C#).
- Confirmed the `.db` file lives at `test-management/data/testmanagement.db`.
- `npm run build` (client, regression check) — still clean, unchanged.
- No seed data of any kind was inserted — `__EFMigrationsHistory` is the
  only non-empty table after migration.
- `automation/`, `dashboard/`, and everything else from Parts 1–4 —
  untouched.

Decision: the verification database was deleted after inspection rather
than committed now — it contains only the empty schema, no real run data,
and deliverable #6 explicitly wants the DB committed once it holds actual
recorded runs (Step 9), not an empty one now. The migration files (the
actual reproducible-schema deliverable for this step) are committed.

STOP after Step 2. Awaiting review before Step 3 (Environment Management).

---

### Step 2 spec (as implemented, kept verbatim below for reference)

### Goal

Implement the persistent domain model for Part 5 using Entity Framework
Core and SQLite.

This step is DATABASE AND DOMAIN MODEL ONLY.

Do not implement Controllers, business APIs, automation execution,
SignalR streaming, dashboard logic, or frontend functionality in this step.

The model must support the future implementation of TM-01 through TM-08
without over-engineering the solution.

---

### 2.1 Environment

Represents a target OffenderWatch environment.

Fields:

- Id
- Name
- BaseUrl
- IsDefault
- CreatedAtUtc
- UpdatedAtUtc

Rules:

- Name is required.
- Name must be unique.
- BaseUrl is required.
- At most one Environment may be marked as default.
  Enforcement of this business rule will be implemented in the
  Environment service/API step.
- Do not hard-code any OffenderWatch environment URL.

Relationship:

Environment 1 -> many TestRuns

Historical runs must survive Environment deletion or later edits.

Therefore TestRun must also store an immutable snapshot of the
environment name and base URL used for that run.

The TestRun -> Environment relationship must not cause historical
TestRuns to be deleted.

---

### 2.2 TestRun

Represents one execution of the automation suite.

Fields:

- Id
- EnvironmentId (nullable FK)
- EnvironmentNameSnapshot
- BaseUrlSnapshot
- Status
- Trigger
- CreatedAtUtc
- StartedAtUtc (nullable)
- EndedAtUtc (nullable)
- PassedCount
- FailedCount
- ExpectedFailedCount
- SkippedCount

Run Status values:

- Queued
- Running
- Completed
- Stopped
- Failed

Trigger values:

- Manual
- API

Do not implement Scheduled trigger yet because scheduled execution is
an optional bonus.

EnvironmentNameSnapshot and BaseUrlSnapshot represent the exact target
used when the run was created.

They must not change if the Environment record is later renamed,
edited, or deleted.

PassedCount, FailedCount, ExpectedFailedCount and SkippedCount are
run-summary snapshots that will be finalized when execution completes.

Do not implement that calculation in Step 2.

Relationships:

Environment 1 -> many TestRuns
TestRun 1 -> many ScenarioResults
TestRun 1 -> many TestDataRecords

Deleting an Environment must never cascade-delete historical TestRuns.

---

### 2.3 TestCase

Represents the stable identity of an automated test across multiple runs.

Fields:

- Id
- ExternalId
- Name
- Suite
- RequirementId (nullable)
- BugId (nullable)
- CreatedAtUtc

ExternalId:

- Required
- Unique
- Must eventually map to the stable runner identity.
- For pytest this will normally be based on pytest nodeid.
- For Playwright this will eventually be based on the test file/title
  identity.

Suite values:

- UI
- API

The same TestCase record must be reused across runs so historical
results can be compared.

Do not create a separate TestHistory table.

Test history will be derived from ScenarioResults belonging to the same
TestCase.

RequirementId and BugId are metadata only.

Known `[BUG-xxx]` scenarios must remain distinguishable later during
runner integration.

---

### 2.4 ScenarioResult

Represents the result of one TestCase in one TestRun.

Fields:

- Id
- TestRunId
- TestCaseId
- Status
- StartedAtUtc (nullable)
- EndedAtUtc (nullable)
- DurationMs (nullable)
- FailureMessage (nullable)
- StackTrace (nullable)

Scenario Status values:

- Queued
- Running
- Passed
- Failed
- ExpectedFail
- Skipped
- Cancelled

Relationships:

TestRun 1 -> many ScenarioResults
TestCase 1 -> many ScenarioResults
ScenarioResult 1 -> many EvidenceArtifacts

Add a unique constraint/index on:

(TestRunId, TestCaseId)

so one TestCase has one ScenarioResult per run.

ScenarioResult is the historical execution record.

A result from a completed historical run must never be replaced by a
result from a later run.

During an active run, its ScenarioResult may transition:

Queued -> Running -> final status

This does not violate the append-only history requirement because later
runs create new ScenarioResult records instead of overwriting previous
run history.

TM-04 history such as regression, recovery, last pass, and
still-failing-since will be derived later by comparing chronological
ScenarioResults for the same TestCase.

Do not implement history calculation in Step 2.

---

### 2.5 EvidenceArtifact

Represents immutable evidence belonging to one ScenarioResult.

Fields:

- Id
- ScenarioResultId
- Type
- RelativePath
- ContentType
- SizeBytes
- CreatedAtUtc

Evidence Type values:

- Log
- Screenshot
- ApiRequest
- ApiResponse
- Trace

Storage decision:

Binary evidence must be stored on disk under:

test-management/artifacts/

SQLite stores only artifact metadata and the relative path.

Do NOT store screenshots or other large binary evidence as SQLite BLOBs.

The future artifact structure should follow the general pattern:

artifacts/
  {runId}/
    {scenarioResultId}/
      ...

Do not implement artifact writing or serving in Step 2.

Rationale:

- keeps SQLite small
- makes evidence easy to inspect
- supports immutable historical artifacts
- is simple to explain and maintain

Evidence must eventually remain associated with the exact
Run + ScenarioResult that produced it.

---

### 2.6 TestDataRecord

Represents application data created by automated scenarios and tracked
for later cleanup.

Fields:

- Id
- TestRunId
- ScenarioResultId (nullable)
- EntityType
- ExternalId (nullable)
- Identifier (nullable)
- CreatedAtUtc
- CleanedAtUtc (nullable)
- CleanupStatus

EntityType must support at least:

- Offender
- LocationPoint

CleanupStatus values:

- Active
- Cleaned
- CleanupFailed

Relationships:

TestRun 1 -> many TestDataRecords
ScenarioResult 1 -> many TestDataRecords (optional relationship)

Test data records belong to the run that created them.

The actual cleanup operation will be implemented later.

IMPORTANT SAFETY RULE:

The original seeded OffenderWatch data must NEVER be deleted.

Future cleanup logic must operate only on data explicitly registered as
test-created data.

Where applicable, the existing AUTO identifier convention should be
used as an additional safety guard, not as the sole ownership mechanism.

Do not implement cleanup logic in Step 2.

---

### 2.7 DbContext

Create the EF Core DbContext containing:

- Environments
- TestRuns
- TestCases
- ScenarioResults
- EvidenceArtifacts
- TestDataRecords

Configure relationships and indexes explicitly.

Important constraints:

- Environment.Name unique
- TestCase.ExternalId unique
- ScenarioResult(TestRunId, TestCaseId) unique
- Environment deletion must not cascade-delete TestRuns
- TestRun deletion should not be part of normal application behavior
- Historical execution data is intended to be retained

Use sensible maximum string lengths where appropriate.

Do not add unnecessary generic repository patterns.

EF Core itself is sufficient for data access at this stage.

---

### 2.8 SQLite Configuration

Configure SQLite through application configuration.

The database connection string must not be embedded directly in C# code.

Use a connection string similar in intent to:

Data Source=../data/offenderwatch-tests.db

but resolve the database path robustly so it works regardless of the
shell's current working directory.

The final SQLite database must live under:

test-management/data/

Do not commit generated temporary database files unless intentionally
needed later as part of the final assignment deliverables.

---

### 2.9 EF Migration

Create the initial EF Core migration for the complete Step 2 schema.

Use a clear migration name such as:

InitialTestManagementSchema

Verify that the migration can create a fresh SQLite database.

The migration files must be committed to source control.

Do not manually create database tables outside EF migrations.

---

### 2.10 UTC

Persist timestamps in UTC.

Use UTC consistently throughout the backend.

Do not introduce local-time persistence.

The frontend may eventually convert UTC timestamps for display.

---

### 2.11 Seed Data

Do NOT seed fake:

- runs
- results
- evidence
- test data
- dashboard data

No fake execution history should be created.

If an Environment is needed for development, prefer creating it later
through TM-01 rather than hard-coding production/demo data into the
migration.

---

### 2.12 Verification

Before completing Step 2:

1. Run:

   dotnet build

2. Create/apply the initial migration to a fresh SQLite database.

3. Verify that all expected tables exist.

4. Verify the important indexes and foreign keys.

5. Verify that the database is created under:

   test-management/data/

6. Run the React build as a regression check:

   npm run build

7. Do not implement Step 3.

---

### 2.13 Expected Result

At the end of Step 2 the project must have a real persistent schema
supporting:

Environment
    |
    +-- TestRun
           |
           +-- ScenarioResult -- TestCase
           |       |
           |       +-- EvidenceArtifact
           |
           +-- TestDataRecord

No functional UI or business API for these entities is required yet.

---

### Step 2 Definition of Done

Step 2 is DONE only when:

- domain entities exist
- enums/statuses exist
- DbContext exists
- SQLite is configured
- relationships are configured
- required unique indexes exist
- initial EF migration exists
- migration successfully creates the database
- backend builds successfully
- frontend still builds successfully
- no fake execution data was introduced
- existing Parts 1-4 remain unaffected

After verification:

Update this document to mark Step 2 DONE and set Current Step to
"Awaiting review".

STOP.

Do not begin Step 3.

Expected concepts include:

- Environment
- TestRun
- TestCase
- ScenarioResult
- EvidenceArtifact
- TestDataRecord

Exact schema and relationships will be defined before implementation.

---

## Step 3 — Environment Management (TM-01)

**STATUS: DONE (2026-08-31). TM-01 -> DONE.**

Implemented the full vertical slice (React -> EnvironmentController ->
EnvironmentService -> TestManagementDbContext -> SQLite) exactly per 3.1–3.10.
First confirmed Steps 1–2 were still intact (`dotnet build` + `npm run build`
clean before starting).

Files added:

- `DTOs/EnvironmentDtos.cs` — `EnvironmentResponseDto`,
  `CreateEnvironmentRequest`, `UpdateEnvironmentRequest`. No EF entity is
  ever returned from the API.
- `Services/IEnvironmentService.cs` / `EnvironmentService.cs` — owns every
  rule in 3.3/3.4/3.5. Validation and the default-Environment invariants
  live here, not in the controller or the frontend.
- `Services/EnvironmentServiceExceptions.cs` —
  `EnvironmentValidationException` (400), `EnvironmentNotFoundException`
  (404), `EnvironmentConflictException` (409, duplicate name).
- `Controllers/EnvironmentController.cs` — the 6 required endpoints, thin
  (no logic beyond calling the service and shaping the HTTP response).
- `Program.cs` — registered `IEnvironmentService`; added a small
  `UseExceptionHandler` block that maps the three exception types above to
  their status codes in one place (`{ title, status, detail }` JSON body)
  instead of try/catch in every controller action.
- `client/src/types/environment.ts`, `client/src/api/environments.ts` — the
  typed API layer (3.9): `getEnvironments`, `getEnvironment`,
  `createEnvironment`, `updateEnvironment`, `deleteEnvironment`,
  `setDefaultEnvironment`, all through `VITE_API_BASE_URL`.
- `client/src/api/client.ts` — extended with `apiRequest<T>()` (shared fetch
  wrapper, parses the backend's `{title,status,detail}` body on error) and
  `ApiError`.
- `client/src/components/EnvironmentFormModal.tsx` — the Add/Edit form
  (3.7). Client-side checks are a convenience; the server's own validation
  message is what's shown on rejection.
- `client/src/pages/EnvironmentsPage.tsx` — the full page (3.6/3.8/3.10):
  table (Name / Base URL / Default badge / Actions), loading state, an
  error banner with Retry for a failed initial load, a separate
  action-error banner for failed create/update/delete/set-default,
  `window.confirm("Delete environment '<name>'?")` before deleting, and a
  refresh-triggering reload after every mutation.
- `test-management/server.Tests/` (new xUnit project, sibling to `server/`,
  not nested inside the plan's original layout — kept separate so the main
  project's own build/publish never picks up test code) —
  `EnvironmentServiceTests.cs`, 13 tests (6 required by 3.13 plus a few
  extra edge cases folded into the same file, e.g. a `[Theory]` covering 4
  distinct invalid-BaseUrl shapes). Each test gets its own throwaway SQLite
  file under the OS temp dir (`EnsureCreated()`, not migrations — fine for
  an isolated test schema), created and deleted per test; never touches
  `test-management/data/testmanagement.db` or any Part 3 OffenderWatch data.

Design decisions / deviations:

- **Transactions**: `Create` (when explicitly requesting default on a
  non-first environment), `Delete` (when it was the default and a
  replacement is promoted), and `SetDefault` each wrap their unset+set (or
  delete+promote) in `Database.BeginTransactionAsync()` — 3.4 rule 4
  ("changing the default must be atomic") and 3.4 rule 6 together.
- **Delete-behavior reuse from Step 2**: deleting an Environment relies
  entirely on the FK's existing `ON DELETE SET NULL` (Step 2) plus the
  snapshot fields already frozen on each TestRun at creation time — the
  delete path never touches `TestRuns` directly, exactly as 3.5 specifies.
- **Uniqueness**: case-insensitive via `Name.ToLower() == name.ToLower()`,
  translated by EF's SQLite provider into SQL `lower()` — confirmed by the
  duplicate-name test using a different case (`"staging"` vs `"Staging"`).
- **xUnit test project location**: `test-management/server.Tests/` (sibling
  to `server/`) rather than inside it — the Step-2-era planned tree didn't
  include a test project since Step 2 had no service logic to test yet;
  this is the natural place to add it now that Step 3 introduces one.

STOP after Step 3. Awaiting review before Step 4 (Run Management &
Automation Integration).

Verified — Backend (3.12 items 1–2 + the bullet list):

- `dotnet build` (server) — 0 warnings, 0 errors.
- Started the API against a **fresh** migrated SQLite DB (deleted, then
  `dotnet ef database update`) and drove the full checklist with `curl`:
  - `POST` env #1 with no `isDefault` → created **with `isDefault: true`**
    (auto-default rule).
  - `POST` env #2 with no `isDefault` → created `isDefault: false`; #1
    unchanged.
  - `PUT /2/default` → #2 becomes default; `GET` list shows exactly one
    `isDefault: true`.
  - `POST` a duplicate name (different case, `"staging"` vs `"Staging"`) →
    **409**, `{"title":"Conflict",...}`.
  - `POST` an invalid `baseUrl` (`"not-a-url"`) → **400**,
    `{"title":"Validation failed",...}`.
  - `PUT /1` renaming + changing the URL → 200, persisted (`GET /1`
    reflects the new values).
  - `GET /999` → **404**, `{"title":"Not found",...}`.
  - Created a 3rd environment, then: delete the non-default one → 204, the
    other two unaffected; delete the (now) default one while one remains →
    204, the remaining environment is **automatically promoted** to
    default; delete the final environment → 204, `GET` list returns `[]`
    (zero environments, zero defaults — valid per rule 7).
- **Directly in SQLite** (not just via the API): created environments A, B
  (`isDefault: true` on create), C — queried
  `SELECT COUNT(*) FROM Environments WHERE IsDefault=1` → **1**, confirming
  no operation sequence ever produces more than one default row.
- `dotnet test` (`server.Tests`) — **13/13 passed**.

Verified — Frontend (3.12 items 3–6):

- `npm run build` (`tsc -b && vite build`) — clean, no type errors. (One
  fix needed: `ApiError`'s constructor originally used a TS parameter
  property, which this project's `erasableSyntaxOnly` tsconfig setting
  rejects — rewritten as a plain field assignment.)
- Ran the API (`dotnet run`, `:5174`) and the Vite dev server (`npm run
  dev`, `:5173`) together and drove the real page in a real Chromium
  browser (Playwright, reusing the Part 3 UI suite's existing install —
  the driver script was a throwaway, deleted after, not part of any
  deliverable):
  - Add "Dev" with no default checked → row appears, **Default badge**
    shown (first-environment rule, confirmed in the actual UI this time,
    not just the API).
  - Add "Staging" → 2 rows.
  - Attempt to add "staging" (duplicate, different case) → form stays
    open, shows the server's own message: *"An environment named 'staging'
    already exists."*
  - Click **Set default** on Staging → Staging gets the badge, Dev loses
    it.
  - Edit "Dev" → "Dev Renamed" → table updates.
  - **Reload the browser page** → both rows still present, Staging still
    shows Default — confirms persistence survives a refresh (3.12 item 6).
  - Click **Delete** on "Dev Renamed" → native confirm dialog reads
    exactly *"Delete environment 'Dev Renamed'?"*, accepted → row count
    drops to 1.
- Environment/Runs/Tests/Test-data nav and the dashboard health indicator
  from Step 1 were not touched — Runs/Tests/Test-data pages remain
  placeholders per the 3.11 scope boundary.

Not implemented (correctly, per the 3.11 scope boundary): Start/Stop Run,
any Playwright/pytest execution, ScenarioResults, SignalR streaming,
history calculations, evidence capture, test-data cleanup, dynamic
dashboard statistics.

Housekeeping: deleted the dev SQLite DB and its `-shm`/`-wal` files after
verification (same reasoning as Step 2 — no real run data yet, nothing
meaningful to commit before Step 9).

---

### Step 3 spec (as implemented, kept verbatim below for reference)

Status: READY FOR IMPLEMENTATION

### Goal

Implement TM-01 Environment Configuration as the first complete
end-to-end Part 5 feature.

The user must be able to manage target OffenderWatch environments
through the React UI.

Flow:

React
  |
  v
EnvironmentController
  |
  v
EnvironmentService
  |
  v
TestManagementDbContext
  |
  v
SQLite

This step implements Environment management only.

Do not implement test-run execution yet.

---

### 3.1 Environment API

Create:

- EnvironmentController
- EnvironmentService
- Environment DTOs

Do not expose EF entities directly from the API.

Use DTOs for requests and responses.

Required endpoints:

GET /api/environments

Returns all active environments.

GET /api/environments/{id}

Returns one environment.

POST /api/environments

Creates an environment.

PUT /api/environments/{id}

Updates an environment.

DELETE /api/environments/{id}

Deletes an environment.

PUT /api/environments/{id}/default

Marks the selected environment as the default.

Use appropriate HTTP status codes:

- 200
- 201
- 204
- 400
- 404
- 409 where appropriate

---

### 3.2 DTOs

Create simple DTOs such as:

EnvironmentResponseDto

Fields:

- Id
- Name
- BaseUrl
- IsDefault
- CreatedAtUtc
- UpdatedAtUtc

CreateEnvironmentRequest

Fields:

- Name
- BaseUrl
- IsDefault

UpdateEnvironmentRequest

Fields:

- Name
- BaseUrl

Do not allow clients to directly control persistence-only fields such as
CreatedAtUtc or UpdatedAtUtc.

Default selection should primarily be handled through:

PUT /api/environments/{id}/default

If IsDefault is accepted during creation, it must still follow all
default-environment invariants.

---

### 3.3 Validation

Environment Name:

- required
- trim whitespace
- cannot be empty after trimming
- must be unique
- uniqueness should be treated case-insensitively if practical with SQLite

BaseUrl:

- required
- trim whitespace
- must be a valid absolute HTTP or HTTPS URL
- reject relative URLs
- reject unsupported schemes
- normalize obvious trailing whitespace
- do not hard-code OffenderWatch-specific URLs

Return useful validation errors.

Do not rely only on frontend validation.

The backend is the source of truth.

---

### 3.4 Default Environment Rules

The system must maintain a clear default Environment.

Rules:

1. If the first Environment is created, it becomes default automatically.

2. If a new Environment is explicitly created as default:
   - unset the previous default
   - set the new Environment as default

3. PUT /api/environments/{id}/default:
   - unset the existing default
   - mark the selected Environment as default

4. Changing the default must be atomic.

5. There must never be more than one default Environment.

6. If non-default Environments exist, deleting the current default
   must select another remaining Environment as the new default.

7. If the final Environment is deleted, zero default Environments is
   valid because no Environments remain.

Do not attempt to enforce this only in React.

The EnvironmentService must enforce these rules.

Use a database transaction where multiple Environment records are
changed as one operation.

---

### 3.5 Environment Deletion & Historical Runs

Environment deletion is allowed.

Existing historical TestRuns must survive.

The Step 2 relationship already uses SET NULL for:

Environment -> TestRun

and TestRun stores:

- EnvironmentNameSnapshot
- BaseUrlSnapshot

Therefore deleting an Environment must:

- remove the Environment
- leave historical TestRuns intact
- set their EnvironmentId to null through the configured FK behavior
- preserve their environment snapshots

Do not manually delete or modify historical TestRuns.

---

### 3.6 Environment React Page

Implement the existing:

/environments

route as a functional page.

The page must load Environments from the API.

Display at least:

- Name
- Base URL
- Default status
- Actions

Actions:

- Add
- Edit
- Delete
- Set as Default

The currently default Environment must be clearly identifiable.

Example concept:

Environments

--------------------------------------------------
Name       Base URL                    Default
--------------------------------------------------
Demo       https://example.com/demo    Default
Staging    https://example.com/stg

Actions:
Edit | Delete | Set Default

Do not hard-code environment rows.

All displayed data must come from the API.

---

### 3.7 Create / Edit UI

Provide a simple form or modal for creating and editing an Environment.

Fields:

- Name
- Base URL

For creation, optionally allow:

- Make Default

Frontend validation should provide immediate feedback for obvious
missing fields.

Backend validation remains authoritative.

Keep the UI simple and professional.

Do not add a large UI framework unless already necessary.

---

### 3.8 Delete UI

Deleting an Environment must require confirmation.

Example:

"Delete environment 'Staging'?"

If deleting the default Environment while others exist, the backend
will automatically choose another remaining Environment as default.

After deletion:

- refresh/update the Environment list
- show the new default correctly

Do not implement historical run UI in this step.

---

### 3.9 API Client

Create a small typed frontend API layer under:

client/src/api/

Do not scatter raw fetch calls throughout React components.

Environment API functions should include the equivalent of:

- getEnvironments()
- getEnvironment(id)
- createEnvironment(...)
- updateEnvironment(...)
- deleteEnvironment(id)
- setDefaultEnvironment(id)

Use the existing:

VITE_API_BASE_URL

configuration.

Do not hard-code the backend URL.

---

### 3.10 Error & Loading States

The Environment page must handle:

- initial loading
- API unavailable
- validation error
- duplicate name
- not found
- delete failure
- successful create/update/delete/default change

Do not silently swallow API errors.

Keep feedback simple.

---

### 3.11 Scope Boundary

Step 3 DOES NOT implement:

- Start Run
- Stop Run
- Playwright execution
- pytest execution
- scenario results
- SignalR execution streaming
- history calculations
- evidence capture
- test data cleanup
- dynamic dashboard statistics

The Runs, Tests and Test Data pages remain placeholders.

Dashboard remains only the Step 1 health-status implementation.

---

### 3.12 Verification

Backend:

1. dotnet build

2. Start the API against a fresh migrated SQLite database.

Verify:

- create first Environment -> automatically Default
- create second Environment -> first remains Default
- create/set another Environment as Default -> old one is unset
- duplicate Name is rejected
- invalid BaseUrl is rejected
- edit Name/BaseUrl works
- GET list returns persisted data
- GET by id works
- unknown id returns 404
- delete non-default works
- delete default when another Environment exists assigns a new default
- delete final Environment leaves zero environments/defaults

Verify directly in SQLite that no operation creates more than one
IsDefault=true row.

Frontend:

3. Run:

npm run build

4. Run API + Vite together.

5. Verify manually in the browser:

- list
- add
- edit
- delete
- set default
- validation/error feedback

6. Refresh the browser and verify the Environment data persists.

---

### 3.13 Automated Backend Tests

Add focused automated tests for EnvironmentService or the Environment
API.

At minimum cover:

- first Environment automatically becomes default
- only one default exists after changing default
- duplicate name rejected
- invalid BaseUrl rejected
- deleting default selects another default when possible
- deleting final Environment works

Use a separate temporary/test SQLite database.

Do not use or modify the real Part 3 OffenderWatch application data.

These tests are tests of the Part 5 platform itself, not replacements
for the Part 3 automation.

Keep the test suite small and focused.

---

### Step 3 Definition of Done

Step 3 is DONE only when:

- Environment CRUD API works
- default selection API works
- backend validation works
- only one default can exist through normal application operations
- Environment deletion preserves historical design
- React Environment page works against the real API
- no Environment data is hard-coded
- Environment data persists in SQLite
- backend automated tests pass
- dotnet build passes
- npm run build passes
- existing Parts 1-4 remain unaffected

TM-01 may be marked DONE after all of the above are verified.

After verification:

Update PART5_PLAN.md:

- Step 3 -> DONE
- TM-01 -> DONE
- Current Step -> Awaiting review

STOP.

Do not begin Step 4.

---

## Step 4 — Run Management & Automation Integration

Implement the core of TM-02.

Integrate the real Part 3 suites.

Details will be defined before implementation.

---

## Step 5 — Real-Time Execution

Implement TM-03 using SignalR.

Details will be defined before implementation.

---

## Step 6 — History & Evidence

Implement TM-04 and TM-08.

Details will be defined before implementation.

---

## Step 7 — Test Data Lifecycle

Implement TM-06.

Details will be defined before implementation.

Seeded application data must NEVER be deleted.

---

## Step 8 — Dynamic Dashboard

Implement TM-07.

Dashboard must derive its information from stored run history.

It will include:

- latest run per environment
- pass-rate trend
- currently failing tests
- failing-since information
- Go / No-Go information

---

## Step 9 — Verification & Submission

Verify:

- at least 3 recorded runs
- at least 2 environments
- at least one regression or recovery
- at least one historical failed scenario with viewable evidence
- README setup instructions
- database migrations
- clean-machine setup

---

# 9. Development Rules

1. Implement one step at a time.
2. Never automatically continue to the next step.
3. Inspect existing code before modifying it.
4. Preserve Parts 1–4.
5. Never fake test results.
6. Never change assertions simply to make tests green.
7. Preserve expected failures / known defects.
8. Do not hard-code target environment URLs.
9. Keep history append-only.
10. Prefer simple, explainable architecture.
11. Avoid unnecessary abstractions and libraries.
12. Every important implementation decision should be explainable
    during the interview.
13. Update this document when an architectural decision changes.
14. Update requirement status only after verification.

---

# 10. Current Step

CURRENT STEP: Awaiting review

Step 3 (Environment Management / TM-01) is DONE and verified — see the
Step 3 section above. Steps 1 and 2 remain DONE.

Do not implement Step 4 or later without review.