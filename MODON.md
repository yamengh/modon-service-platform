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

**Current milestone (2):** sign-in, the service catalogue, and a working **Excavation Works Permit**
application: drafts, the full form, six required attachments, submission and request tracking.
The Building Licence is in the catalogue but its application opens in a later milestone, as do the admin
portal, the mobile app and Camunda.

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
- creates the Modon request tables (PostgreSQL: EF Core migrations; SQLite: created once),
- creates the dropdown options (industrial cities, permit types, …),
- creates development accounts and their establishments (Development environment only).

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
| `contractor@modon.local` | contractor | ✅ — can apply for the Excavation Works Permit (establishment CR `7012345678`) |
| `consultant@modon.local` | consultant | ✅ |
| `reviewer@modon.local` | modon_reviewer | ❌ (shown a "no access" message — reviewers will use the admin portal) |
| `sadmin@modon.local` | FormCMS super admin | ❌ (for FormCMS administration only) |

Roles cannot be self-assigned: new sign-ups get no Modon role, and only a FormCMS super admin can change roles.

## 5b. Try the excavation permit (step by step)

1. Sign in as `contractor@modon.local`.
2. **خدمات مدن** → **تصريح أعمال الحفر** → **بدء الخدمة**. A draft is created with a number like `EXC-000001-15-10-26` (prefix-id-day-month-year).
3. Fill in some fields and press **حفظ كمسودة** (save as draft). You can leave and come back from **طلباتي**.
4. Fill in every field marked `*`, and add all six attachments (one file each, max 20,480 KB;
   the work-location photo must be JPEG/JPG/PNG, the others JPEG/JPG/PNG/PDF).
5. Press **تقديم الطلب** (submit). The request becomes **قيد التنفيذ** (in progress) and can no longer be edited.
6. **طلباتي** lists your requests; open one to see its details, files and status history.

The dropdown values other than the industrial cities are **placeholders** (the manual doesn't list them).
A FormCMS super admin can change them in the `modonLookup` entity without code changes.

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
                                              └─ Modon:   personas, requests, attachments, seeding
                                                           (own tables "modon_*", EF Core)
```

| Endpoint | Provided by | Purpose |
|---|---|---|
| `POST /api/login`, `GET /api/logout` | FormCMS | Sign in / out (cookie) |
| `GET /api/modon/me/personas` | Modon | Who am I, and which portal personas do I have? (401 if signed out) |
| `GET /api/queries/modonServices` | FormCMS saved query | Service list (public, cached) |
| `GET /api/queries/modonService/single?code=…` | FormCMS saved query | One service's details |
| `GET /api/queries/modonLookups?category=…` | FormCMS saved query | Dropdown options |
| `GET /api/modon/me/establishments` | Modon | Establishments (commercial registrations) the user may act for |
| `POST /api/modon/requests` | Modon | Start a service → creates a draft (`{ "serviceCode": "excavation-permit" }`) |
| `GET /api/modon/requests` · `GET /api/modon/requests/{id}` | Modon | My requests · one request with files and history |
| `PUT /api/modon/requests/{id}/excavation` | Modon | Save the draft form (format checks only) |
| `POST /api/modon/requests/{id}/submit` | Modon | Submit (all required fields and attachments) |
| `POST · DELETE · GET /api/modon/requests/{id}/attachments/{slot}` | Modon | Upload (multipart `file`) · remove · download |

Security rules enforced by the backend:

- Users only ever see their own requests (other people's requests return 404).
- Only contractors can apply for the excavation permit, and only for an establishment they are linked to.
- Statuses change only through the workflow (`Draft → InProgress` in this milestone); every change is recorded in the status history.
- Uploaded documents are stored **outside** `wwwroot` (`server/Modon.Platform/App_Data/private-documents`)
  and can only be downloaded by their owner. Size, extension, MIME type and file signature are checked.
- State-changing `/api/modon` calls need the header `X-Modon-Client` (the portal sends it), which blocks cross-site form posts.

The catalogue is a normal FormCMS entity (`modonService`), so its text can be edited later from the
FormCMS admin tools without code changes. The seeder only adds missing services; it never overwrites edits.

## Troubleshooting

| Problem | Fix |
|---|---|
| `Failed to connect to 127.0.0.1:5433` | PostgreSQL isn't running: start Docker Desktop, then `docker compose up -d`. Or use `--launch-profile http-sqlite`. |
| `error during connect … dockerDesktopLinuxEngine` | Docker Desktop isn't started. Open it and wait until it says "running". |
| Port 5100 or 5173 already in use | Stop the other program, or close an old terminal still running the backend/portal. |
| Portal shows "Could not load data" | The backend isn't running on port 5100. |
| Want a clean database | PostgreSQL: `docker compose down -v` (deletes data). SQLite: delete `server/Modon.Platform/modon-dev.db*`. Uploaded files: delete `server/Modon.Platform/App_Data/`. |
| SQLite error about a missing column after updating the code | The SQLite option does not upgrade existing tables. Delete `server/Modon.Platform/modon-dev.db*` and restart. (PostgreSQL upgrades itself through migrations.) |
| `dotnet build` says a file is locked by `Modon.Platform` | The backend is still running. Stop it (Ctrl+C in its terminal) and build again. |

## For developers: changing the Modon tables

The request tables are defined in `server/Modon.Platform/Data/ModonDbContext.cs`. After changing the model,
add a PostgreSQL migration from the repository root (the `dotnet-ef` tool is pinned in `dotnet-tools.json`):

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> --project server/Modon.Platform --output-dir Data/Migrations --context ModonDbContext
```

It is applied automatically the next time the backend starts on PostgreSQL.
