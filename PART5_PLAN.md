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
| TM-01 | Environment configuration | Planned |
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

TO BE DEFINED BEFORE IMPLEMENTATION.

Do not implement until the data model has been reviewed and approved.

Expected concepts include:

- Environment
- TestRun
- TestCase
- ScenarioResult
- EvidenceArtifact
- TestDataRecord

Exact schema and relationships will be defined before implementation.

---

## Step 3 — Environment Management

Implement TM-01.

Details will be defined before implementation.

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

CURRENT STEP: Step 1 — Foundation — DONE, awaiting review.

Do not implement Step 2 or later until this is reviewed and approved.