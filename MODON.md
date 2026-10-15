# Modon Service Platform — Getting Started

This repository is **FormCMS** plus a Modon layer built on top of it. The FormCMS code under
`server/FormCMS*` is used as-is; all Modon code lives in new folders:

| Folder | What it is |
|---|---|
| `server/Modon.Platform/` | The backend (ASP.NET Core, .NET 10). It hosts FormCMS and adds Modon features. |
| `server/Modon.Platform.Tests/` | Backend tests (xUnit). They start the real backend on a temporary SQLite database. |
| `apps/portal/` | Investor / partner web portal (React + Vite + TypeScript, Arabic first). |
| `docker-compose.yml` | PostgreSQL for local development. |
| `modon-platform.sln` | Solution with only FormCMS + Modon projects (open this one in your IDE). |

**Current milestone (1):** sign-in and the service catalogue (Building Licence & Construction Reports,
Excavation Works Permit). Applying for a service, the admin portal, the mobile app and Camunda come later.

---

## 1. Install the tools (once)

| Tool | Version used | Check with |
|---|---|---|
| .NET SDK | 10.x | `dotnet --version` |
| Node.js | 22.x | `node --version` |
| Docker Desktop | any recent | `docker --version` (Docker Desktop must be **running**) |

Docker is only needed for PostgreSQL. You can skip it and use SQLite (see step 2, option B).

## 2. Start the database

**Option A — PostgreSQL (recommended).** From the repository root:

```bash
docker compose up -d
```

This starts PostgreSQL on **port 5433** (not 5432, so it won't clash with other databases, e.g. your
Camunda setup). Data is kept in a Docker volume between restarts. Stop it with `docker compose down`.

**Option B — SQLite (no Docker).** Nothing to start; the backend creates `modon-dev.db` itself.
Use the `http-sqlite` profile in step 3.

## 3. Start the backend

```bash
cd server/Modon.Platform
dotnet run                               # PostgreSQL (option A)
dotnet run --launch-profile http-sqlite  # SQLite (option B)
```

Wait for `Now listening on: http://localhost:5100`. On first start it automatically:

- creates the database tables (FormCMS + user accounts),
- creates the roles `investor`, `contractor`, `consultant`, `modon_reviewer`,
- creates the service catalogue and its two services,
- creates development accounts (Development environment only).

Quick check in a browser: <http://localhost:5100/api/queries/modonServices> should show both services as JSON.

## 4. Start the web portal

In a **second** terminal:

```bash
cd apps/portal
npm install      # first time only
npm run dev
```

Open <http://localhost:5173>. The portal forwards `/api` calls to the backend on port 5100.

## 5. Development accounts

All use the password **`Modon@Dev1`** (development only — set in `appsettings.Development.json`).

| Email | Role | Portal access |
|---|---|---|
| `investor@modon.local` | investor | ✅ |
| `contractor@modon.local` | contractor | ✅ |
| `consultant@modon.local` | consultant | ✅ |
| `reviewer@modon.local` | modon_reviewer | ❌ (shown a "no access" message — reviewers will use the admin portal) |
| `sadmin@modon.local` | FormCMS super admin | ❌ (for FormCMS administration only) |

Roles cannot be self-assigned: new sign-ups get no Modon role, and only a FormCMS super admin can change roles.

## 6. Run the tests

```bash
# backend (no Docker needed — uses a temporary SQLite file)
dotnet test server/Modon.Platform.Tests/Modon.Platform.Tests.csproj

# portal
cd apps/portal
npm test
```

## How the pieces fit

```
Browser ──► Vite dev server :5173 ──/api──► Modon.Platform :5100 ──► PostgreSQL :5433
                                              ├─ FormCMS: login cookie, roles, entities, saved queries
                                              └─ Modon:   /api/modon/me/personas, seeding
```

| Endpoint | Provided by | Purpose |
|---|---|---|
| `POST /api/login`, `GET /api/logout` | FormCMS | Sign in / out (cookie) |
| `GET /api/modon/me/personas` | Modon | Who am I, and which portal personas do I have? (401 if signed out) |
| `GET /api/queries/modonServices` | FormCMS saved query | Service list (public, cached) |
| `GET /api/queries/modonService/single?code=…` | FormCMS saved query | One service's details |

The catalogue is a normal FormCMS entity (`modonService`), so its text can be edited later from the
FormCMS admin tools without code changes. The seeder only adds missing services; it never overwrites edits.

## Troubleshooting

| Problem | Fix |
|---|---|
| `Failed to connect to 127.0.0.1:5433` | PostgreSQL isn't running: start Docker Desktop, then `docker compose up -d`. Or use `--launch-profile http-sqlite`. |
| `error during connect … dockerDesktopLinuxEngine` | Docker Desktop isn't started. Open it and wait until it says "running". |
| Port 5100 or 5173 already in use | Stop the other program, or close an old terminal still running the backend/portal. |
| Portal shows "Could not load data" | The backend isn't running on port 5100. |
| Want a clean database | PostgreSQL: `docker compose down -v` (deletes data). SQLite: delete `server/Modon.Platform/modon-dev.db*`. |
