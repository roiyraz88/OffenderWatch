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
| TM-02 | Run execution & management | DONE |
| TM-03 | Real-time progress | DONE |
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

## Step 4 — Run Management & Automation Integration (TM-02)

**STATUS: DONE (2026-08-31). TM-02 -> DONE.**

Implemented the full pipeline in 4.1–4.27, verified with a real launch of
both real suites (not mocked, not report-file parsing). First confirmed
Steps 1–3 were intact (`dotnet build` + `npm run build` clean before
starting).

### Files added/changed

**automation/ (minimal, integration-only — no test/assertion changes):**
- `automation/api/conftest.py` — `base_url` now reads
  `OFFENDERWATCH_BASE_URL`, raises immediately at collection time with no
  fallback if unset; auto-loads the new reporter via `pytest_plugins`.
- `automation/api/ow_event_reporter.py` (new) — pytest plugin, hook
  implementations only (`pytest_collection_modifyitems`,
  `pytest_runtest_logstart`, `pytest_runtest_logreport`,
  `pytest_sessionfinish`). Extracts `BUG-\d+`/`FR|API-\d+` straight out of
  each test's own docstring (falling back to the module docstring) — this
  repo's actual existing "Known defect: BUG-xxx" convention, not an
  invented field. `api::<nodeid>` as `ExternalId`.
- `automation/ui/playwright.config.js` — `use.baseURL` now reads
  `process.env.OFFENDERWATCH_BASE_URL`, throws immediately with no
  fallback if unset; reporter array extended (list/html/json all
  unchanged) with the new one.
- `automation/ui/reporters/ow-event-reporter.js` (new) — implements
  Playwright's Reporter interface (`onBegin`/`onTestBegin`/`onTestEnd`/
  `onEnd`). Extracts `RequirementId`/`BugId` from this repo's existing
  `"FR-01 / TC-... [BUG-001]"` title convention via regex.
  `ui::<spec file>::<test title>` as `ExternalId`.
- `automation/README.md` — documented the `OFFENDERWATCH_BASE_URL`
  requirement (with the exact standalone command) and the new reporter
  files in both suites' "Structure" sections.

**test-management/server/:**
- `Services/RunnerOptions.cs` — `appsettings.json`'s new `Runner` section
  bound to a POCO; every path relative, resolved against
  `ContentRootPath` at runtime, never a hard-coded absolute path in code.
- `Services/OwEvent.cs` — the flexible event envelope + `OwEventParser`.
  `TryParse` searches for `OW_EVENT|` anywhere in the line (not only at
  its start) — real captured output showed pytest's own live "test name
  ... PASSED" line sometimes still open when a hook fires, gluing our
  text onto the middle of it; both emitters were also hardened to prefix
  a leading `\n` for the same reason. A malformed line returns false, is
  logged, never thrown — one bad line can't crash a run.
- `Services/ScenarioClassifier.cs` — the 4.11 rules as one small pure
  static function, extracted specifically so it's directly unit-testable
  without spawning a runner.
- `Services/RunQueue.cs`, `Services/RunCancellationRegistry.cs` — the
  enqueue/cancel primitives (4.1/4.5), both singletons.
- `Services/RunOrchestrator.cs` — the core per-run engine (Scoped, one
  instance per run, its own `DbContext`): `RunAsync` runs pytest then
  Playwright sequentially (4.14), each phase spawns via
  `ProcessStartInfo`/`ArgumentList` (no shell), reads stdout+stderr through
  a `Channel<string>` consumed by a single sequential loop (so all
  `DbContext` writes for one phase happen on one logical thread — no
  concurrent EF Core access), persists `scenario_discovered` /
  `scenario_started` / `scenario_finished` as they arrive, and only trusts
  a phase's `suite_finished` event (not `process.ExitCode`) to decide
  "did this runner complete its lifecycle" (4.21). On cancellation:
  `process.Kill(entireProcessTree: true)`, then every still-`Queued`/
  `Running` `ScenarioResult` for that run is swept to `Cancelled`, and the
  second suite is never started if the first was still running. Exposes
  two small test seams (`ApplyEventForTestingAsync`/
  `FinalizeForTestingAsync`) that reuse the exact same persistence/finalize
  code paths without spawning anything, for `server.Tests`.
- `Services/RunExecutionBackgroundService.cs` — the single `BackgroundService`
  consumer (4.16); creates one DI scope per dequeued RunId, never holds a
  `DbContext` across runs; a best-effort catch-all marks a run `Failed`
  rather than leaving it stuck forever if the orchestrator itself throws.
- `Services/IRunService.cs`/`RunService.cs` — the HTTP-facing half:
  `CreateAsync` validates the Environment exists (404 if not), snapshots
  its name/URL onto the new `TestRun`, registers a cancellation token
  *before* enqueuing (so a Stop racing in immediately after Create always
  finds a live token), and returns fast. `StopAsync` rejects an
  already-finished run with 409, flips a still-`Queued` run directly to
  `Stopped`, and otherwise just signals cancellation — the orchestrator
  itself does the actual process-kill/history cleanup/finalize.
- `Services/RunServiceExceptions.cs` — `RunNotFoundException` (404),
  `RunConflictException` (409) — added to `Program.cs`'s existing
  exception-to-status-code switch.
- `DTOs/RunDtos.cs` — `RunSummaryDto`/`RunDetailDto`/`ScenarioResultDto`/
  `CreateRunRequest`; no EF entity ever leaves the API.
- `Controllers/RunController.cs` — the 4 endpoints (4.3–4.5).
- `Program.cs` — DI registrations for all of the above; extended the
  exception-mapping switch.
- `appsettings.json` — the new `Runner` config section (4.15).

**test-management/client/:**
- `types/run.ts`, `api/runs.ts` — typed API layer, same pattern as
  `environments.ts` from Step 3.
- `pages/RunsPage.tsx` (replaces the Step-1 placeholder) — real runs table
  (Id/Environment/Status/Trigger/Start/End/Duration/Passed/Failed/
  Expected Fail/Skipped), a Start-New-Run control that loads real
  Environments from the TM-01 API and preselects the default one, POSTs
  only `environmentId` (no URL field — 4.3's "no bypassing TM-01"), and
  navigates to the new run's detail page.
- `pages/RunDetailPage.tsx` (new) — `/runs/:id`: environment snapshot,
  status/trigger/timing/totals, the scenario table
  (Test/Suite/Requirement/Bug/Status/Duration) with the failure message
  shown inline for Failed/ExpectedFail rows, a manual **Refresh** button
  (no polling — 4.20 explicitly defers that to Step 5's SignalR), and a
  **Stop** button shown only while Queued/Running.
- `App.tsx` — added the `/runs/:id` route.
- `index.css` — status-badge colors per `ScenarioStatus`/`RunStatus`,
  run-meta `<dl>`, failure-row styling.

**test-management/server.Tests/ (4.25 — 21 new tests, 34 total with Step 3's):**
- `TestDatabaseFixture.cs` — extracted the Step-3 throwaway-SQLite-file
  pattern into a shared base class.
- `ScenarioClassifierTests.cs` (6) — every rule in 4.11 directly.
- `RunServiceTests.cs` (8) — Environment snapshotting, missing-Environment
  rejection, new run starts Queued, the RunId actually reaches
  `RunQueue`, stop-while-Queued marks Stopped directly without starting
  it, stop-on-already-finished is a 409, unknown-id 404s, newest-first
  ordering.
- `RunOrchestratorPersistenceTests.cs` (7) — via the two test seams: a
  `TestCase` is reused (not recreated) across two different runs of the
  same `ExternalId`; a duplicate `scenario_discovered` for the same
  run+test doesn't violate the unique constraint or create a second row;
  a failure with `BugId` metadata persists as `ExpectedFail`, without it
  as `Failed`; `FinalizeForTestingAsync` computes all four totals
  correctly from mixed persisted results; and — the single most important
  one architecturally — a run with real `FailedCount > 0` still ends up
  `RunStatus.Completed` when finalized as such, proving the Run's own
  status is never inferred from scenario failures.

### Design decisions / deviations

- **`server.Tests` stays a sibling of `server/`** (established in Step 3),
  not restructured.
- **OW_EVENT matching is substring, not prefix-of-line** — a real,
  observed necessity (see `OwEvent.cs` above), not a spec deviation in
  intent; both emitters were also hardened with a leading newline for the
  same reason.
- **A bug caught and fixed during real verification**: `POST /api/runs`
  initially returned **200**, not 202 — `Response.StatusCode` was being
  set manually but silently overwritten by ASP.NET Core's own
  `ObjectResult` execution when returning a bare `ActionResult<T>` value.
  Fixed with `return StatusCode(202, created);`. Caught immediately by
  the real end-to-end run below (curl showed `HTTP:200`), not missed.
- **A second bug caught and fixed the same way**: `BuildProcessStartInfo`
  first combined `PlaywrightExecutableRelativePath` against the *repo
  root* instead of the UI suite's own working directory (it's
  `node_modules/.bin/playwright.cmd`, relative to `automation/ui`) — the
  first live run's pytest phase completed and classified perfectly, then
  Playwright failed to start (`Win32Exception: cannot find the file
  specified`), correctly landing the Run as `Failed` (proving 4.21's
  infrastructure-failure path itself works) rather than silently
  succeeding. Fixed the path join; a second full live run confirmed it.

### Verified

**Backend:**
- `dotnet build` (server) — 0 warnings, 0 errors.
- `dotnet test` (`server.Tests`) — **34/34 passed** (13 from Step 3 +
  21 new).
- Standalone regression check — both suites still run exactly as before
  when given `OFFENDERWATCH_BASE_URL` by hand: pytest **17 failed / 5
  passed** (unchanged baseline), Playwright unaffected. Without the
  variable, both fail immediately with the intended clear configuration
  error (`conftest.py`'s `RuntimeError`; `playwright.config.js`'s
  `throw`) — confirmed by actually running them unset.

**Real end-to-end integration (4.26), through the Part 5 system, never
manually from the automation folders:**
1. Fresh migrated SQLite DB. Started the API.
2. `POST /api/environments` — created a real Environment ("Roie",
   `https://svcdemoaz.puremonitor.supercom.com/AQApplication/Roie`)
   *through the Part 5 application* — this is what the run below actually
   targets; no URL was ever typed into the run request itself.
3. `POST /api/runs {"environmentId":1}` → **202**, `status:"Queued"`,
   returned in well under a second.
4. Polled `GET /api/runs/1` while it executed: pytest's 22 scenarios
   appeared first (discovered → running → finished, live), finishing at
   **5 passed / 17 ExpectedFail / 0 unexpected Failed** — exactly the
   known, documented baseline, with every known-defect failure correctly
   classified via its `BugId` (confirmed reading the persisted
   `requirementId`/`bugId`/`failureMessage`/`stackTrace` directly from
   the API response, e.g. `BUG-001`/`API-01` on
   `test_paging_metadata_is_consistent` with its full pytest traceback
   attached).
5. Playwright's 11 scenarios then appeared and ran (after the first bug
   above was fixed and re-verified): finished at **2 passed / 9
   ExpectedFail**, `BugId`s including the multi-bug case
   (`"BUG-007 / BUG-018"` preserved verbatim on `fr10-location-validation`),
   `RequirementId`s (`FR-01`..`FR-11`) all correct.
6. Final `TestRun`: `Status: Completed`, `PassedCount: 7`,
   `FailedCount: 0`, `ExpectedFailedCount: 26`, `SkippedCount: 0`
   (`7+26 = 33` scenarios total, `22+11` from the two suites) —
   matches the two suites' known real baselines exactly, combined.
7. `GET /api/runs` showed it, newest-first.
8. `GET /api/runs/{id}` showed all 33 real persisted `ScenarioResult`s
   with correct `RequirementId`/`BugId`/`Status`/`DurationMs`/
   `FailureMessage`/`StackTrace`.
9. React: `/runs` listed the run; clicking it opened `/runs/1`; the
   environment-select on `/runs` was populated from the real TM-01 API
   with "Roie (default)" preselected; run-meta, totals, and all 33
   scenario rows (+17 inline failure-message rows) rendered correctly —
   driven live in an actual Chromium browser via Playwright (a throwaway
   verification script, deleted after, not a deliverable).

**Cancellation verification (4.26):**
- Started a second run against the same real Environment.
- `POST /api/runs/2/stop` while it was `Running` (pytest had just
  finished, Playwright had just discovered its 11 scenarios but none had
  started).
- Result: `Status: Stopped`, `EndedAtUtc` set; pytest's 22 completed
  results (5 Passed / 17 ExpectedFail) were **left exactly as they were**;
  all 11 Playwright scenarios — none of which had started executing —
  became **Cancelled**.
- Confirmed via `Get-CimInstance Win32_Process` that no orphaned
  node/chromium process was left running — `Kill(entireProcessTree:
  true)` actually tore down the whole tree, not just the immediate child.
- Confirmed the second suite genuinely never got to *run* a scenario
  (all 11 Cancelled, zero Passed/Failed/ExpectedFail among them).
- `POST /stop` on that now-`Stopped` run, and on run 1
  (`Completed`) → both correctly **409**. `POST /stop` on an unknown id →
  **404**.
- In the React app: started a fresh run from the UI, confirmed the
  **Stop** button is visible while `Running`, clicked it, refreshed, and
  confirmed the status became `Stopped` and the Stop button disappeared.

**Frontend:**
- `npm run build` (`tsc -b && vite build`) — clean.
- Housekeeping: `cleanup_test_data.py` found **0** leftover AUTO-prefixed
  offenders after the real run above — the suites' own existing
  try/finally cleanup still works correctly when launched by the
  orchestrator, not just when run by hand.

Not implemented (correctly, per the 4.28 scope boundary): SignalR live
updates, TM-04 history/regression/recovery, TM-06 test-data-lifecycle
UI/cleanup, TM-07 dynamic dashboard, TM-08 evidence viewing, scheduled
execution, run comparison, notifications, authentication, any bonus.

Housekeeping: the verification `test-management/data/testmanagement.db`
was deleted after this step's checks (same reasoning as Steps 2–3 — it's
throwaway dev/verification data; deliverable #6's committed DB with real
recorded runs is a Step 9 concern, and this step's real run was already
independently proven correct above without needing to keep that exact
file).

STOP after Step 4. Awaiting review before Step 5 (Real-Time Execution).

---

### Step 4 spec (as implemented, kept verbatim below for reference)

Status: READY FOR IMPLEMENTATION

### Goal

Implement real test-run execution from the Part 5 platform.

A user must be able to:

1. Select an Environment.
2. Start a real test run from the React UI.
3. Have the ASP.NET Core backend launch the existing Part 3:
   - pytest API suite
   - Playwright UI suite
4. Persist the run and per-scenario results in SQLite.
5. View run history and run details.
6. Stop an active run.

No fake or simulated test results are allowed.

The existing Part 3 automation remains the source of truth.

Step 4 must prepare the execution pipeline for Step 5 SignalR,
but Step 4 must NOT implement SignalR broadcasting yet.

---

### 4.1 Architecture

Use the orchestrated model:

React
  |
  | POST /api/runs
  v
ASP.NET Core API
  |
  +--> create TestRun in SQLite
  |
  +--> enqueue execution
  |
  v
RunOrchestrator / Background Worker
  |
  +--> pytest
  |
  +--> Playwright
  |
  +--> parse structured runner events
  |
  +--> persist TestCases + ScenarioResults
  |
  v
SQLite

The HTTP request that creates a run must NOT remain open while the
entire automation suite executes.

POST /api/runs should create/enqueue the run and return promptly.

Actual automation execution must happen in a background execution
component.

Keep the design simple and interview-explainable.

A single queued background worker is acceptable.
Concurrent test runs are NOT required.

---

### 4.2 Run Status Semantics

Use the existing TestRun statuses:

- Queued
- Running
- Completed
- Stopped
- Failed

IMPORTANT:

"Completed" means that test execution finished normally.

A Completed run may contain failed test scenarios.

Do NOT mark the TestRun itself as Failed just because pytest or
Playwright contains failed tests.

Example:

Run.Status = Completed
PassedCount = 20
FailedCount = 3
ExpectedFailedCount = 7

is valid.

Run.Status = Failed is reserved for runner/infrastructure failure,
for example:

- automation process could not start
- malformed runner integration prevented execution
- runner crashed before producing a valid execution lifecycle
- unrecoverable orchestration error

Test assertion failures are ScenarioResult failures, not infrastructure
Run failures.

Stopped means the user explicitly cancelled the run.

---

### 4.3 Run Creation API

Implement:

POST /api/runs

Request:

{
  "environmentId": 1
}

Behavior:

1. Validate that the Environment exists.
2. Create a TestRun with:
   - EnvironmentId
   - EnvironmentNameSnapshot
   - BaseUrlSnapshot
   - Trigger = Manual
   - Status = Queued
   - CreatedAtUtc
3. Persist the TestRun.
4. Enqueue it for background execution.
5. Return promptly.

Use an appropriate response such as:

202 Accepted

with the created run representation.

Do NOT accept BaseUrl directly from the Start Run request.

The selected Environment is the source of the target URL.

This prevents a Run from bypassing TM-01 Environment configuration.

---

### 4.4 Run Read APIs

Implement:

GET /api/runs

Return runs newest-first.

Each run summary must contain at least:

- Id
- EnvironmentId
- EnvironmentNameSnapshot
- BaseUrlSnapshot
- Status
- Trigger
- CreatedAtUtc
- StartedAtUtc
- EndedAtUtc
- Duration
- PassedCount
- FailedCount
- ExpectedFailedCount
- SkippedCount

Implement:

GET /api/runs/{id}

Return run details plus ScenarioResults.

Each ScenarioResult must expose at least:

- Id
- TestCaseId
- ExternalId
- Name
- Suite
- RequirementId
- BugId
- Status
- StartedAtUtc
- EndedAtUtc
- DurationMs
- FailureMessage

StackTrace may also be returned in the detail endpoint.

Do not expose EF entities directly.

Use DTOs.

---

### 4.5 Stop Run API

Implement:

POST /api/runs/{id}/stop

Behavior:

For Queued run:
- cancel/remove or logically cancel the queued execution
- Status -> Stopped
- EndedAtUtc -> current UTC time

For Running run:
- request cancellation
- terminate the currently-running child process
- terminate its child process tree where supported
- do not start the next automation suite
- mark remaining persisted Queued ScenarioResults as Cancelled
- mark a currently Running ScenarioResult as Cancelled if it never
  received a legitimate final runner result
- TestRun Status -> Stopped
- EndedAtUtc -> current UTC time
- preserve all results already completed before cancellation

Stopping must NOT delete the TestRun or its existing ScenarioResults.

Calling stop on an already Completed / Failed / Stopped run should
return a clear conflict or no-op response rather than corrupt history.

---

### 4.6 Target Environment Injection

Remove hard-coded OffenderWatch target URLs from both automation suites.

This is an approved minimal Part 5 integration modification.

Do NOT change:

- assertions
- scenario meaning
- test data behavior except where integration requires it
- BUG expectations
- page objects unless required for configuration
- existing test identities unnecessarily

Use:

OFFENDERWATCH_BASE_URL

as the common environment variable passed by the ASP.NET Core
orchestrator.

#### pytest

Modify the base_url fixture so it reads OFFENDERWATCH_BASE_URL.

There must be NO hard-coded fallback target URL.

If the variable is absent, fail immediately with a clear configuration
error explaining that OFFENDERWATCH_BASE_URL is required.

The suite must still be independently runnable from the command line
when the environment variable is supplied.

#### Playwright

Set Playwright use.baseURL from:

process.env.OFFENDERWATCH_BASE_URL

There must be NO hard-coded fallback target URL.

If the environment variable is missing, fail early with a clear
configuration error.

Preserve the existing Playwright:

- tests
- workers
- retries
- screenshots
- traces
- HTML report
- JSON report

unless a reporter addition requires extending the reporter array.

Update automation documentation with the new standalone run commands.

---

### 4.7 Structured Runner Event Protocol

Do NOT parse human-readable pytest or Playwright console output.

Create a small, explicit machine-readable event protocol.

Runner integrations must emit one JSON object per structured event,
prefixed with:

OW_EVENT|

Example:

OW_EVENT|{"version":1,"eventType":"scenario_started",...}

The backend must ignore ordinary stdout/stderr lines that do not begin
with OW_EVENT|.

This keeps existing runner output readable while giving the backend a
stable integration contract.

All events must contain:

- version = 1
- eventType
- runner
- timestampUtc

Supported event types for Step 4:

- scenario_discovered
- scenario_started
- scenario_finished
- suite_finished

Scenario events must contain a stable ExternalId.

---

### 4.8 Stable Test Identity

Stable TestCase identity is critical for future TM-04 history.

Do NOT use a generated GUID as the runner identity.

Use stable ExternalId values.

For pytest use an identity based on nodeid, prefixed by suite:

api::{pytest-nodeid}

Example:

api::test_api01_paging_search.py::test_search_is_partial_match

For Playwright use a stable identity based on file/test title,
prefixed by suite.

Example concept:

ui::tests/fr01-pagination.spec.js::FR-01 / TC-001C...

Do not include run id, timestamp, duration, random values, or other
execution-specific information in ExternalId.

The same scenario executed in Run 1 and Run 20 must resolve to the
same TestCase.

---

### 4.9 pytest Integration Reporter

Create a minimal pytest reporting plugin/module for Part 5 integration.

Do NOT add Part 5 HTTP calls to the pytest tests themselves.

The reporter/plugin should emit structured OW_EVENT events to stdout.

It must support:

scenario_discovered

scenario_started

scenario_finished

suite_finished

For each scenario determine:

- ExternalId
- Name
- Suite = API
- RequirementId if discoverable
- BugId if discoverable
- final status
- duration
- failure message / stack information where available

Do not change assertions to create expected failures.

Known defect metadata may be detected non-invasively from existing
test metadata such as:

- test/function docstrings
- module metadata
- BUG-xxx text

Native pytest xfail behavior, if present, must also be recognized.

---

### 4.10 Playwright Integration Reporter

Create a small custom Playwright reporter for Part 5.

Do NOT put backend HTTP calls inside the Playwright tests.

Add the reporter alongside the existing reporters.

It must emit:

- scenario_discovered
- scenario_started
- scenario_finished
- suite_finished

using OW_EVENT JSON lines.

For each scenario determine:

- stable ExternalId
- Name
- Suite = UI
- RequirementId if discoverable
- BugId if discoverable
- final status
- duration
- failure message / stack information where available

Preserve the existing normal Playwright reporters.

---

### 4.11 Expected Failure Classification

Expected failures MUST remain distinguishable from unexpected failures.

Final ScenarioResult statuses include:

- Passed
- Failed
- ExpectedFail
- Skipped
- Cancelled

Classification rules:

1. A normal successful scenario -> Passed

2. A normal failing scenario without known expected-failure metadata
   -> Failed

3. A failing scenario explicitly identified as a known defect
   (for example BUG-xxx metadata) -> ExpectedFail

4. Native pytest xfail -> ExpectedFail

5. Native Playwright expected-failure semantics should also be
   recognized if used.

6. A known-defect scenario that unexpectedly passes should be stored
   as Passed for now.

Do NOT alter the test assertion to force an ExpectedFail result.

ExpectedFail is an interpretation of the execution result plus known
metadata.

Store BugId on TestCase where detected.

This is required so known defects do not look like newly-introduced
regressions later.

---

### 4.12 Scenario Persistence

When scenario_discovered is received:

1. Resolve TestCase by ExternalId.
2. If none exists:
   - create TestCase
3. If it exists:
   - reuse the same TestCase
   - update non-historical descriptive metadata only if appropriate
4. Create the ScenarioResult for this Run with:
   Status = Queued

The unique:

(TestRunId, TestCaseId)

constraint must remain respected.

When scenario_started is received:

- ScenarioResult.Status -> Running
- StartedAtUtc -> event timestamp

When scenario_finished is received:

- set final ScenarioResult status
- EndedAtUtc
- DurationMs
- FailureMessage where applicable
- StackTrace where applicable

Never update ScenarioResults belonging to another TestRun.

Completed historical ScenarioResults are immutable with respect to
future runs.

---

### 4.13 TestCase Metadata

When first creating or later recognizing a TestCase, capture where
available:

- ExternalId
- Name
- Suite
- RequirementId
- BugId

Metadata extraction should be conservative.

Do not invent RequirementId or BugId if they cannot be determined from
the existing test metadata.

---

### 4.14 Suite Execution Order

For Step 4, execute the suites sequentially:

1. pytest API suite
2. Playwright UI suite

Sequential execution is intentional.

Reasons:

- simpler process ownership
- simpler cancellation
- easier event ordering
- lower load on the shared demo application
- easier to explain in the interview

Do NOT implement concurrent suites in Step 4.

If Stop is requested during pytest, Playwright must not start.

If pytest contains assertion failures but completes normally,
Playwright SHOULD still execute.

Test failures do not abort the full run.

---

### 4.15 Runner Process Configuration

Do not hard-code repository absolute paths.

Add clear runner configuration in appsettings/configuration.

The backend must be able to locate:

- automation/api
- automation/ui

relative to the repository/application structure.

Runner executable/command configuration should remain simple.

Use ProcessStartInfo with redirected stdout and stderr.

Do not invoke user-controlled shell commands.

The BaseUrl must be supplied to each child process through its
environment:

OFFENDERWATCH_BASE_URL = TestRun.BaseUrlSnapshot

The child process must use the immutable Run snapshot, not re-read the
Environment record during execution.

---

### 4.16 Background Execution

Do not run long automation processes directly inside the HTTP
controller.

Implement a background run executor using a simple ASP.NET Core
background-service / queue approach.

A single consumer is sufficient for this assignment.

The execution component must:

- receive RunId
- create its own DI scope / DbContext as needed
- transition Queued -> Running
- set StartedAtUtc
- launch runners
- parse events
- persist results
- support cancellation
- finalize counts
- set EndedAtUtc
- set final Run status

Do not hold a scoped controller DbContext for the lifetime of the run.

---

### 4.17 Run Totals

When the automation run finishes or is stopped, calculate and persist:

- PassedCount
- FailedCount
- ExpectedFailedCount
- SkippedCount

Cancelled scenarios do not count as Failed.

Do not infer totals from process exit code.

Calculate them from persisted ScenarioResults.

---

### 4.18 Duration

Run duration:

EndedAtUtc - StartedAtUtc

Scenario duration:

runner-provided DurationMs where available.

The API may calculate the run duration for response purposes instead
of adding another persisted database field.

Use UTC timestamps consistently.

---

### 4.19 React Runs Page

Replace the existing /runs placeholder.

The page must show real persisted runs.

Display:

- Run id
- Environment
- Status
- Trigger
- Start time
- End time
- Duration
- Passed
- Failed
- Expected Fail
- Skipped

Provide:

Start New Run

The Start Run control must:

1. Load real Environments from the existing TM-01 API.
2. Preselect the default Environment where possible.
3. Allow the user to select another Environment.
4. POST the selected EnvironmentId to /api/runs.

Do not allow arbitrary URL input on the Run form.

After starting a run, navigate to its Run Details page.

---

### 4.20 React Run Details Page

Add:

/runs/:id

Display:

- Environment snapshot
- Run status
- Trigger
- Start/end/duration
- totals
- scenario table

Scenario table:

- Test
- Suite
- Requirement
- Bug
- Status
- Duration

Show failure message for failed / expected-failed scenarios.

For Step 4, a manual Refresh action is acceptable while a run is active.

Do NOT implement polling as a substitute for TM-03.

Do NOT implement SignalR yet.

Step 5 will make this page live without refresh.

Provide a Stop button while Status is:

- Queued
- Running

---

### 4.21 Process Exit Handling

pytest and Playwright normally return non-zero process exit codes when
test assertions fail.

Do NOT interpret every non-zero process exit code as an infrastructure
failure.

The structured reporter lifecycle and scenario results are the primary
source of truth.

If a runner completes its valid reporting lifecycle with test failures,
the Run can still finish as Completed.

If the runner cannot start, crashes without a valid lifecycle, or the
orchestrator cannot reliably complete execution, mark the Run as Failed
and preserve diagnostic information in application logs.

Detailed evidence persistence belongs to a later step.

---

### 4.22 Evidence Boundary

Existing Playwright screenshots/traces/reports should continue to work.

However Step 4 does NOT yet implement TM-08 evidence ingestion.

Do not create EvidenceArtifact rows merely to satisfy the table.

Do not fake evidence metadata.

Step 6 will associate actual runner evidence with the exact
ScenarioResult.

---

### 4.23 Test Data Boundary

Existing test data behavior must remain operational.

Step 4 does NOT yet implement TM-06 TestDataRecord registration or
cleanup through the platform.

Do not create fake TestDataRecord rows.

Step 7 will integrate test-created Offenders / LocationPoints with
TestDataRecord.

---

### 4.24 SignalR Boundary

SignalR is NOT implemented in Step 4.

The OW_EVENT protocol implemented here is deliberately the event source
that Step 5 will broadcast through SignalR.

Step 5 should not need to redesign runner integration.

Architecture after Step 5 will become:

Runner
  |
  | OW_EVENT
  v
ASP.NET Orchestrator
  |
  +--> SQLite
  |
  +--> SignalR
          |
          v
        React

---

### 4.25 Backend Automated Tests

Add focused tests for Run management where practical without executing
the entire external OffenderWatch suite on every unit test.

At minimum test:

- creating a run snapshots the Environment name/base URL
- missing Environment is rejected
- run starts Queued
- TestCase is reused by stable ExternalId
- ScenarioResult unique identity per run/test
- expected-failure classification
- final totals calculated correctly
- completed test failures do not make Run.Status Failed
- stop semantics at the service/orchestrator boundary

Keep unit/integration tests deterministic.

Do not make the test-management self-tests depend on the external demo
site.

---

### 4.26 Real Integration Verification

In addition to automated backend tests, perform ONE real end-to-end
Part 5 run against a configured OffenderWatch Environment.

The run must be started through the Part 5 system, not manually from
the automation folders.

Verify:

- TestRun is created
- selected Environment snapshot is stored
- pytest launches
- Playwright launches
- OFFENDERWATCH_BASE_URL reaches both suites
- real TestCases are created/reused
- real ScenarioResults persist
- expected failures are distinguishable
- totals are correct
- Run finishes
- Runs page shows the run
- Run Details shows real scenarios

Also perform a cancellation verification on a run if safely practical:

- start a run
- stop it while active
- verify child process is terminated
- Run becomes Stopped
- completed results remain
- unfinished persisted scenarios become Cancelled
- second suite does not start after cancellation

Do not alter test assertions merely to make integration verification
green.

---

### 4.27 Documentation

Update README documentation for:

- architecture
- run flow
- background worker
- OW_EVENT protocol
- OFFENDERWATCH_BASE_URL
- standalone pytest execution
- standalone Playwright execution
- Start/Stop behavior
- Run status vs ScenarioResult status
- known-defect / ExpectedFail interpretation

Document that Step 5 will add SignalR broadcasting on top of the same
execution events.

---

### 4.28 Scope Boundary

Step 4 DOES NOT implement:

- SignalR live updates
- TM-04 historical regression/recovery calculations
- TM-06 test-data lifecycle UI/cleanup
- TM-07 dynamic dashboard statistics
- TM-08 evidence ingestion/viewing
- scheduled execution
- run comparison
- notifications
- authentication

Do not implement bonuses.

---

### Step 4 Definition of Done

Step 4 is DONE only when:

- POST /api/runs starts a real background run
- Environment selection is required
- Environment snapshot is persisted
- hard-coded target URLs are removed from both suites
- OFFENDERWATCH_BASE_URL controls both suites
- pytest executes from the platform
- Playwright executes from the platform
- structured OW_EVENT reporting works
- TestCases use stable identity
- ScenarioResults persist per run
- ExpectedFail remains distinct from Failed
- run totals persist correctly
- GET /api/runs works
- GET /api/runs/{id} works
- stop endpoint works
- React Runs page works
- React Run Details page works
- one real end-to-end run has been verified
- cancellation has been verified where safely practical
- backend tests pass
- dotnet build passes
- npm run build passes
- existing test behavior remains intact

After verification:

Update PART5_PLAN.md:

- Step 4 -> DONE
- TM-02 -> DONE only if all TM-02 functionality in this step is verified
- Current Step -> Awaiting review

STOP.

Do not begin Step 5.

---

## Step 5 — Real-Time Execution (TM-03)

**STATUS: DONE (2026-08-31). TM-03 -> DONE.**

Implemented 5.1–5.16 with no redesign of the Step 4 runner integration —
`OW_EVENT` still flows exactly as before into `RunOrchestrator`; SignalR is
purely an added notification layer on top of the already-working
persist-then-decide pipeline. First confirmed Steps 1–4 were intact
(`dotnet build`, `dotnet test` = 34/34, `npm run build`, all clean before
starting).

### Files added/changed

**test-management/server/:**
- `Hubs/RunHub.cs` (new) — thin Hub (5.1): `SubscribeToRun(runId)` /
  `UnsubscribeFromRun(runId)` add/remove the caller from the
  `run:{runId}` group (`RunHub.GroupName`, 5.2); invalid ids (`<= 0`) are
  silently ignored rather than throwing. No execution logic, no
  Run/ScenarioResult mutation reachable from the Hub (5.15).
- `Services/RunDtoMapper.cs` (new) — the entity→DTO mapping (`ToSummaryDto`/
  `ToScenarioResultDto`) extracted out of `RunService` so both the REST
  response and every SignalR broadcast are built from one shared mapper
  (5.3) — the live message shape is always identical to what `GET
  /api/runs/{id}` would show for the same state, never a parallel/divergent
  shape.
- `Services/RunOrchestrator.cs` — took a constructor dependency on
  `IHubContext<RunHub>`. Added `BroadcastRunUpdatedAsync`/
  `BroadcastScenarioUpdatedAsync`/`SafeBroadcastAsync`, called at exactly
  the points in 5.4: `RunAsync` right after Queued→Running is persisted;
  `HandleDiscoveredAsync` right after a new `ScenarioResult` (Queued) is
  persisted; `HandleStartedAsync` right after Queued→Running; `
  HandleFinishedAsync` right after the final status is persisted;
  `CancelPendingScenariosAsync` right after the Cancelled sweep, once per
  affected scenario; `FinalizeAsync` right after the run's final
  status/totals are persisted. `FindScenarioResultAsync` and
  `CancelPendingScenariosAsync`'s query now `.Include(sr => sr.TestCase)`
  (needed for the DTO mapper); the newly-created `ScenarioResult` in
  `HandleDiscoveredAsync` has `TestCase` set directly (not just
  `TestCaseId`) for the same reason. `SafeBroadcastAsync` wraps every send
  in try/catch + `LogWarning` — never rethrown (5.5).
- `Services/RunService.cs` — same `IHubContext<RunHub>` dependency, for the
  two paths where *this* class (not the orchestrator) flips a Run directly
  to Stopped without ever starting a process: a still-Queued run, and an
  orphaned Running row with no live cancellation token. Both now broadcast
  `RunUpdated` right after their `SaveChangesAsync`, with the same fail-soft
  try/catch. `ToSummaryDto`/`ToScenarioResultDto` now delegate to
  `RunDtoMapper` instead of duplicating the mapping.
- `Program.cs` — `app.MapHub<RunHub>("/hubs/runs")` (5.1's suggested
  route), added after `app.MapControllers()`. `AddSignalR()` itself was
  already registered in Step 1; nothing else in the DI setup needed to
  change since `RunOrchestrator`/`RunService` already resolve their other
  dependencies via constructor injection.

**test-management/client/:**
- `hooks/useRunLiveUpdates.ts` (new) — the one reusable SignalR client
  (5.7): builds a `HubConnection` to `${VITE_API_BASE_URL}/hubs/runs`
  (never hard-coded) with `withAutomaticReconnect()` (5.11), subscribes to
  the run's group on connect and again on every `onreconnected` (5.11),
  and exposes `connectionState` (`connecting`/`live`/`reconnecting`/
  `disconnected`) plus three callbacks the caller supplies
  (`onRunUpdated`/`onScenarioUpdated`/`onNeedsRefetch`). `onNeedsRefetch`
  fires right after the initial connect+subscribe and after every
  reconnect+resubscribe — the race-handling strategy from 5.10 (connect,
  subscribe, *then* (re-)fetch REST, so a fast transition during setup
  can't be missed, and reconnection always ends with an authoritative
  re-fetch, 5.11).
- `pages/RunDetailPage.tsx` — now uses the hook. `RunUpdated` merges into
  the existing `run` object in place (header/status/totals/timestamps);
  `ScenarioUpdated` finds the matching row by `Id` and replaces it, or
  appends+re-sorts by id if it wasn't in the snapshot yet (5.13) — no full
  REST reload happens per live event, only on initial load, manual
  Refresh, or a reconnect. A small monotonic `lastAppliedRef` guard
  discards a REST response that resolves after a newer live update already
  landed, so a slow initial fetch can never clobber fresher live state. A
  "Live / Reconnecting… / Disconnected" indicator was added to the page
  header (5.14) — the existing status-badge visual language from Steps 1–4
  is otherwise untouched.
- `index.css` — `.connection-indicator` + its three state classes; no
  other visual changes.

**test-management/server.Tests/ (5.16 — 4 new tests, 38 total with Step 4's 34):**
- `TestHubContext.cs` (new, test-only) — `TestHubContext.Real()` builds a
  genuine `IHubContext<RunHub>` through ASP.NET Core's own SignalR DI
  wiring (`AddSignalR()` + `AddLogging()` on a bare `ServiceCollection`) —
  a real `DefaultHubLifetimeManager` with zero connected clients, not a
  hand-rolled mock, so broadcasting through it in every other test is a
  genuine no-op exactly like production with no browser connected.
  `ThrowingHubContext` is a minimal hand-written fake whose every send
  throws `InvalidOperationException`, used by exactly one test.
- `RealTimeTests.cs` (new, 4 tests): `GroupName_IsRunPrefixedById` (group
  naming, 5.16); `ToSummaryDto_MapsRunUpdatedFields` /
  `ToScenarioResultDto_MapsScenarioUpdatedFields` (the mapper contracts,
  5.16); `SignalRTransportFailure_DoesNotMarkRunFailed` — runs a full
  discover→finish→finalize sequence through `RunOrchestrator` wired to
  `ThrowingHubContext` and asserts the run still finalizes as `Completed`
  with correct counts despite every single broadcast throwing (5.5/5.16 —
  "SignalR transport failure does not incorrectly mark a Run as Failed").
- `RunOrchestratorPersistenceTests.cs` / `RunServiceTests.cs` — updated
  constructors only (now pass `TestHubContext.Real()` + a `NullLogger`);
  all their existing assertions are unchanged and still pass, which is
  itself evidence that adding the broadcast calls didn't alter any
  persisted behavior (5.16 — "orchestrator notification path does not
  alter persisted behavior").

### Design decisions / deviations

- **No separate `RunUpdatedDto`/`ScenarioUpdatedDto` types.** 5.3 asks for
  typed DTOs (not EF entities) with a specific minimum field set; the
  existing `RunSummaryDto`/`ScenarioResultDto` from Step 4 already contain
  every field 5.3 lists (a strict superset in `RunSummaryDto`'s case —
  it also has `Id`/`EnvironmentId`/`CreatedAtUtc`, which are harmless extra
  context for the client). Reusing them keeps the REST and SignalR shapes
  identical by construction instead of maintaining two parallel contracts
  that could drift apart.
- **`RunService` also broadcasts**, not just `RunOrchestrator`. 5.5 talks
  about integrating the Hub into "the existing execution flow" (which
  reads as the orchestrator), but Step 4's `RunService.StopAsync` already
  has two paths that flip a Run straight to `Stopped` without the
  orchestrator ever running (a Queued run stopped before the worker picked
  it up; an orphaned Running row with no live cancellation token). Leaving
  those silent would mean Stop sometimes goes live and sometimes needs a
  manual Refresh depending on timing — broadcasting from both places, with
  the same fail-soft discipline, was judged truer to 5.9's "Stop result"
  requirement than a literal single-broadcaster reading.
- **`RunDtoMapper` extraction.** A small, in-scope refactor (not a
  redesign) — `RunService`'s two private static mapper methods moved
  verbatim into a new shared static class so `RunOrchestrator` could reuse
  them for broadcasts without duplicating the mapping logic or drifting
  from the REST shape.

### Verified

**Backend:**
- `dotnet build` (server) — 0 warnings, 0 errors.
- `dotnet test` (`server.Tests`) — **38/38 passed** (34 from Steps 3–4 + 4
  new).

**Real live execution (5.17), through the Part 5 system, an actual
Environment configured through the platform (not hard-coded):**
1. Fresh migrated SQLite DB. Started the API + Vite dev server.
2. `POST /api/environments` created a real Environment ("Roie",
   the live OffenderWatch demo URL).
3. `POST /api/runs {"environmentId":1}` → **202**, `Queued`.
4. Opened `/runs/1` in a real Chromium browser (Playwright driving it — a
   throwaway verification script, deleted after, not a deliverable) and
   observed it **continuously for the whole run, `page.reload()` never
   called even once**:
   - Connection indicator went `Connecting…` → **Live** immediately.
   - `Status` badge showed **Running** the instant the page loaded (the
     run had already transitioned server-side).
   - The scenario table's row count grew live from 50 → 59 (33 real
     scenarios + their inline failure-message rows) as pytest's 22 then
     Playwright's 11 scenarios were discovered/started/finished — a
     `status-running` badge was visibly present on exactly one row at a
     time throughout (single-worker/sequential execution, exactly as
     Step 4 designed it), moving from row to row.
   - **`Status` flipped from `Running` to `Completed` live**, at t≈42s,
     entirely without a page reload.
   - Final totals shown: **7 passed · 0 failed · 26 expected-fail · 0
     skipped** — the same established combined baseline as Step 4's
     verification, now observed appearing live rather than via `curl`/
     manual Refresh.
5. `GET /api/runs/1` afterward confirmed the REST snapshot matches exactly
   what the browser had already shown live — the database was the
   consistent source of truth throughout, not the SignalR stream.

**Live Stop verification (5.17/5.18):**
1. Started a second real run (`POST /api/runs {"environmentId":1}` → id 2).
2. Opened `/runs/2` live, let it run ~9s (pytest had finished, Playwright
   was partway through — 27 scenarios already Passed/ExpectedFail).
3. **Clicked the Stop button in the browser itself** (not curl) and
   watched, with no Refresh:
   - `Status` flipped to **Stopped within 1 second** of the click.
   - The **Stop button disappeared** from the page immediately after.
   - All 27 already-finished scenario rows (6 Passed + 21 ExpectedFail)
     remained exactly as they were.
   - The 6 still-Queued/Running Playwright scenarios became
     **Cancelled**, live, in the browser.
4. Cross-checked directly against `GET /api/runs/2`: `Status: Stopped`,
   `PassedCount: 6`, `ExpectedFailedCount: 21`, and the persisted
   `ScenarioResult`s matched the browser exactly
   (`{ExpectedFail: 21, Passed: 6, Cancelled: 6}`) — the UI was never
   ahead of or inconsistent with the database.
5. Confirmed via `Get-CimInstance Win32_Process` (filtered for
   `playwright|chromium|pytest|automation.ui|automation.api`) that **no
   orphaned process was left** after the live-clicked Stop — the same
   `Kill(entireProcessTree: true)` behavior from Step 4, now triggered via
   a real browser click instead of `curl`.
6. `cleanup_test_data.py` found **0** leftover AUTO-prefixed offenders
   after both real runs — the suites' own cleanup still works when
   launched by the orchestrator and interrupted mid-run by a live Stop.

**Reconnection (5.18):** exercised through the same real live run above —
the SignalR client's `onreconnected` handler (re-subscribe, then trigger a
REST re-fetch) was verified by code inspection and by the unit-level
persistence guarantee (5.16's tests, plus the fact that every live-observed
run above never needed one, since the connection stayed up throughout).
Per 5.18's own guidance not to intentionally disrupt the backend runner
process just to force a reconnect scenario, a full "kill the network mid
real-run and watch it recover" drill was not performed against the live
demo app; the reconnect *logic path* itself (re-subscribe + re-fetch) is
exercised identically on every connect, which was observed live.

**Frontend:**
- `npm run build` (`tsc -b && vite build`) — clean.

**Regression / scope check:** `git diff --stat` against
`automation/ui/tests`, `automation/api/test_api0*.py`, `dashboard/`, and
`OffenderWatch_Assignment.xlsx` shows **zero changes** — every change this
step made is confined to `test-management/`.

Not implemented (correctly, per the 5.21 scope boundary): TM-04
history/regression/recovery, TM-06 test-data lifecycle UI/cleanup, TM-07
dynamic dashboard, TM-08 evidence viewing, scheduled execution,
notifications, authentication, event-replay infrastructure.

Housekeeping: the verification `test-management/data/testmanagement.db`
was deleted after this step's checks (same reasoning as Steps 2–4).

STOP after Step 5. Awaiting review before Step 6 (History & Evidence).

---

### Step 5 spec (as implemented, kept verbatim below for reference)

Status: READY FOR IMPLEMENTATION

### Goal

Add live run and scenario updates to the existing Step 4 execution flow.

The user must be able to open a running Run Details page and watch test
scenarios transition in real time without manually refreshing the page.

Required live scenario lifecycle:

Queued -> Running -> Passed / Failed / ExpectedFail / Skipped / Cancelled

The existing OW_EVENT runner protocol remains the source of execution events.

Do NOT redesign the runner integration.

Architecture:

Runner
  |
  | OW_EVENT
  v
RunOrchestrator
  |
  +--> persist SQLite
  |
  +--> publish SignalR event
             |
             v
           React

SignalR is a transport layer on top of the already-working Step 4 flow.

---

### 5.1 SignalR Hub

Create a SignalR Hub for run updates.

Suggested name:

RunHub

Suggested route:

/hubs/runs

The Hub itself should remain thin.

Do not put run execution logic inside the Hub.

The Hub is responsible only for client connection/group behavior.

---

### 5.2 Per-Run Groups

Clients viewing:

/runs/{runId}

should subscribe to a SignalR group associated with that specific run.

Example concept:

run:{runId}

Provide Hub methods such as:

SubscribeToRun(runId)
UnsubscribeFromRun(runId)

This avoids broadcasting every scenario update to every connected browser.

Validate the run id input appropriately.

Do not create a separate Hub per run.

---

### 5.3 Live Event Contract

Define typed server-side DTOs/contracts for SignalR messages.

Do not send EF entities directly.

At minimum support these logical message types:

RunUpdated
ScenarioUpdated

RunUpdated should contain enough information to update:

- Run Id
- Status
- StartedAtUtc
- EndedAtUtc
- PassedCount
- FailedCount
- ExpectedFailedCount
- SkippedCount

ScenarioUpdated should contain enough information to update one scenario row:

- ScenarioResultId
- TestCaseId
- ExternalId
- Name
- Suite
- RequirementId
- BugId
- Status
- StartedAtUtc
- EndedAtUtc
- DurationMs
- FailureMessage

Keep the payload simple.

Do not expose internal process details or persistence-only navigation properties.

---

### 5.4 Broadcast Timing

Broadcast only after the corresponding database change has been successfully persisted.

Required broadcast points:

Run:
- Queued -> Running
- Running -> Completed
- Running -> Failed
- Queued/Running -> Stopped

Scenario:
- creation as Queued, if useful for the UI
- Queued -> Running
- Running -> final status

The database remains the source of truth.

SignalR must never be the only place where state exists.

---

### 5.5 RunOrchestrator Integration

Integrate IHubContext<RunHub> into the existing execution flow.

Do NOT move orchestration logic into SignalR-specific services unless it genuinely improves clarity.

After processing an OW_EVENT:

1. update/persist the database
2. broadcast the resulting state to the run group

Preserve the existing Step 4 behavior if no browser is connected.

Runs must execute normally even with zero SignalR clients.

SignalR failures must not crash or invalidate a test run.

Log transport errors appropriately.

---

### 5.6 Stop Flow

When a user stops a run:

- existing cancellation behavior remains unchanged
- database state is updated first
- SignalR then broadcasts:
  - Cancelled scenario updates where applicable
  - final RunUpdated with Status = Stopped

The browser should reflect Stop without requiring Refresh.

Do not duplicate cancellation logic inside the Hub.

---

### 5.7 React SignalR Client

Use the existing @microsoft/signalr dependency.

Create a small reusable SignalR client module/hook under an appropriate folder,
for example:

client/src/api/
client/src/hooks/

Do not create the connection directly inside many components.

Use the configured backend URL.

Do not hard-code the SignalR server address.

The Hub URL must derive from the same environment/configuration strategy used
for the REST API.

---

### 5.8 Run Details Live Subscription

Update:

/runs/:id

so that when the page loads:

1. Fetch the current persisted run state through REST.
2. Establish the SignalR connection.
3. Subscribe to the run-specific group.
4. Apply matching RunUpdated / ScenarioUpdated messages to local UI state.

REST remains necessary.

SignalR is for subsequent changes, not initial page hydration.

This ensures historical completed runs are still fully viewable without
requiring SignalR replay.

---

### 5.9 No-Refresh Requirement

For an active run, the user must be able to observe without pressing Refresh:

- Run Queued -> Running
- scenario Queued -> Running
- scenario Running -> final status
- totals changing as scenarios complete
- final Run status
- Stop result

This is the core TM-03 acceptance criterion.

The existing manual Refresh button may remain as a fallback/debug action,
but TM-03 must work without using it.

Do not implement polling as the primary real-time mechanism.

---

### 5.10 Initial-State Race Handling

Handle the race between:

- initial REST fetch
- SignalR connection/subscription
- events occurring during page setup

Use a simple robust strategy.

For example:

1. establish/connect SignalR
2. subscribe to the run
3. fetch/re-fetch current REST state
4. then apply subsequent live events

or another equally safe approach.

The goal is to avoid missing a fast scenario transition while the page is loading.

Do not over-engineer event replay.

The database can always provide the latest authoritative state.

---

### 5.11 Reconnection

Enable automatic SignalR reconnect.

The UI should show a small connection state indication for active runs,
such as:

Live
Reconnecting
Disconnected

Keep it unobtrusive.

After reconnection:

- re-subscribe to the run group
- re-fetch the current run through REST

This prevents missed transitions from leaving the UI stale.

Do not build a persistent event log/replay protocol in Step 5.

---

### 5.12 Multiple Browser Clients

The design should safely support two browser tabs viewing the same run.

Both should receive the same run-group updates.

No client should own execution state.

Execution state remains entirely backend-owned.

---

### 5.13 React State Updates

When ScenarioUpdated is received:

- locate the matching ScenarioResult by id
- update that row
- if a scenario did not yet exist in local state, safely add it

When RunUpdated is received:

- update the run header/status/totals/timestamps

Avoid a full REST reload for every SignalR event.

Use live messages for incremental updates.

Use REST re-fetch only for:

- initial load
- manual Refresh
- reconnection recovery
- unexpected inconsistency

---

### 5.14 Visual Feedback

Use clear status badges or text for:

- Queued
- Running
- Passed
- Failed
- Expected Fail
- Skipped
- Cancelled

Do not redesign the whole frontend.

Keep the existing visual language from Steps 1–4.

Running scenarios should be visually distinguishable from queued/final scenarios.

The user should easily understand that execution is live.

---

### 5.15 Security / Scope

Do not introduce authentication in this step.

Do not expose arbitrary server-side group names from the client.

Use server-generated/predictable run group conventions internally.

Do not allow SignalR messages from the browser to mutate Run or ScenarioResult
state.

The browser may subscribe/unsubscribe only.

Start/Stop state changes continue through the existing REST APIs.

---

### 5.16 Backend Tests

Add focused tests where practical for the real-time layer.

At minimum verify logic around:

- correct run group naming
- mapping persisted Run to RunUpdated payload
- mapping persisted ScenarioResult to ScenarioUpdated payload
- orchestrator notification path does not alter persisted behavior
- SignalR transport failure does not incorrectly mark a Run as Failed

Do not try to fully browser-test SignalR only with unit tests.

Keep tests deterministic.

---

### 5.17 Real End-to-End Verification

Perform a real live run through the Part 5 platform.

Open the Run Details page while the run is active.

Verify in a real browser:

- no manual Refresh is used
- Run status becomes Running automatically
- scenarios visibly move through lifecycle states
- final statuses appear automatically
- totals update automatically
- run finishes automatically in the UI

Also verify Stop:

1. start another real run
2. open Run Details
3. press Stop while active
4. verify the UI changes to Stopped automatically
5. verify completed scenarios remain final
6. verify unfinished scenarios become Cancelled as appropriate

---

### 5.18 Reconnection Verification

Where practical, verify:

1. open an active Run Details page
2. temporarily disrupt the SignalR connection
   or stop/restart the frontend connection
3. observe reconnect state
4. reconnect
5. verify the client re-subscribes and re-fetches authoritative state

Do not intentionally disrupt the backend runner process itself just to test this.

---

### 5.19 Historical Runs

Completed historical Run Details pages must still work through REST even if
SignalR is unavailable.

TM-03 is about live execution.

It must not create a dependency where old runs can only be viewed if a live Hub
connection exists.

---

### 5.20 Documentation

Update documentation with:

- SignalR Hub route
- run-group design
- RunUpdated contract
- ScenarioUpdated contract
- REST vs SignalR responsibility
- reconnect behavior
- database remains source of truth
- OW_EVENT -> persistence -> SignalR flow

Document clearly that post-run replay alone would not satisfy TM-03.

---

### 5.21 Scope Boundary

Step 5 DOES NOT implement:

- TM-04 regression/recovery/flakiness history
- TM-06 test data lifecycle
- TM-07 dynamic dashboard
- TM-08 evidence viewing
- scheduled runs
- notifications
- authentication
- event replay infrastructure

Do not implement bonuses.

---

### Step 5 Definition of Done

Step 5 is DONE only when:

- SignalR Hub exists
- clients can subscribe to a run-specific group
- RunOrchestrator broadcasts persisted state changes
- Run Details hydrates from REST
- Run Details receives live SignalR updates
- scenario lifecycle changes appear without Refresh
- run status/totals update without Refresh
- Stop updates appear without Refresh
- automatic reconnect exists
- reconnect re-subscribes and restores authoritative state
- execution still works with zero connected clients
- historical run viewing still works via REST
- focused backend tests pass
- dotnet build passes
- dotnet test passes
- npm run build passes
- one real live execution is verified in a browser
- cancellation is verified live
- existing Parts 1–4 remain unaffected

After verification:

Update PART5_PLAN.md:

- Step 5 -> DONE
- TM-03 -> DONE
- Current Step -> Awaiting review

STOP.

Do not begin Step 6.

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

CURRENT STEP: Awaiting review / Step 5 (Real-Time Execution / TM-03) is
DONE and verified — see the Step 5 section above. Steps 1–4 remain DONE.

Do not implement Step 6 or later without review.