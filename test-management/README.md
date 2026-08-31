# OffenderWatch — Test Management Platform (Part 5)

Status: **Step 1 — Foundation** only. See [`PART5_PLAN.md`](../PART5_PLAN.md)
at the repo root for the full implementation plan and current step.

No business functionality is implemented yet — this is the project
scaffold (server, client, tooling) that later steps build on.

## Structure

- `server/` — ASP.NET Core Web API (.NET 8, C#)
- `client/` — React + Vite + TypeScript
- `data/` — SQLite database file (created once Step 2 adds the data model)
- `artifacts/` — evidence files (screenshots, logs) once Step 6 adds capture

## Run locally (Step 1 scaffold)

**Server** (Swagger at `/swagger`, health check at `/api/health`):

```bash
cd server
dotnet run
```

Listens on `http://localhost:5174` by default (see
`server/Properties/launchSettings.json`).

**Client**:

```bash
cd client
npm install
cp .env.example .env.local   # points VITE_API_BASE_URL at the server above
npm run dev
```

Listens on `http://localhost:5173` by default — matches the server's
`ClientOrigins` CORS config in `server/appsettings.json`.

Full setup instructions, the database schema rationale, and the
automation-integration model will be documented here as each step lands.
