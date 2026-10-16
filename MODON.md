# Modon Service Platform — Getting Started

This repository is **FormCMS** plus a Modon layer built on top of it. The FormCMS code under
`server/FormCMS*` is used as-is; all Modon code lives in new folders:

| Folder | What it is |
|---|---|
| `server/Modon.Platform/` | The backend (ASP.NET Core, .NET 10). It hosts FormCMS and adds Modon features. |
| `server/Modon.Platform.Tests/` | Backend tests (xUnit). They start the real backend on a temporary SQLite database. |
| `apps/portal/` | Investor / partner web portal (React + Vite + TypeScript, Arabic first) — port 5173. |
| `apps/admin/` | Modon admin / reviewer portal (same stack and conventions) — port 5174. |
| `docker-compose.yml` | PostgreSQL for local development. |
| `modon-platform.sln` | Solution with only FormCMS + Modon projects (open this one in your IDE). |

**Current milestone (3):**

- **Partner portal** — sign-in, the service catalogue, and a working **Excavation Works Permit** application
  (drafts, the full form, six required attachments, submission and request tracking).
- **Admin portal** — staff sign-in, a dashboard of request counts by status, a filterable request queue,
  full request details (form, applicant, attachments, audit history), and review decisions:
  **approve**, **return for completion** (comment required) and **reject** (reason required).

Not done yet: the applicant can *see* a "returned for completion" status and the reviewer's comment, but
**editing and resubmitting a returned request, and the coordination letters, come in Milestone 4**.
The Building Licence application, the mobile app and Camunda come later as well.

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

## 4b. Start the admin (reviewer) portal

In a **third** terminal:

```bash
cd apps/admin
npm install      # first time only
npm run dev
```

Open <http://localhost:5174> and sign in as `reviewer@modon.local` / `Modon@Dev1`.

> Both portals run on `localhost`, and browsers share cookies between ports of the same host. Signing in
> on one portal therefore signs you in (as the same user) on the other. Use a private window for the
> second portal if you want to be a contractor and a reviewer at the same time.

## 5. Development accounts

All use the password **`Modon@Dev1`** (development only — set in `appsettings.Development.json`).

| Email | Role | Portal access |
|---|---|---|
| `investor@modon.local` | investor | ✅ |
| `contractor@modon.local` | contractor | ✅ — can apply for the Excavation Works Permit (establishment CR `7012345678`) |
| `consultant@modon.local` | consultant | ✅ |
| `reviewer@modon.local` | modon_reviewer | ❌ partner portal · ✅ **admin portal** (review requests) |
| `sadmin@modon.local` | FormCMS super admin | ❌ partner portal · ✅ admin portal (super admin has full data access in FormCMS) |

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

## 5c. Review a request (admin portal)

1. Submit an excavation request as `contractor@modon.local` (step 5b).
2. In the admin portal (<http://localhost:5174>) sign in as `reviewer@modon.local`.
3. The **dashboard** shows counts per status; click **قيد التنفيذ** to open the queue filtered to new requests.
   The queue can be filtered by status and service and searched by request number.
4. Open the request: you see the form, the applicant and establishment, the attachments (download links)
   and the status history.
5. Choose a decision: **اعتماد** (approve, optional note), **إعادة لإكمال النواقص** (return — a comment is
   required) or **رفض** (reject — a reason is required). The decision, your account and the time are
   recorded in the history, and the applicant sees the new status and comment in **طلباتي**.

## 6. Run the tests

```bash
# backend (no Docker needed — uses a temporary SQLite file)
dotnet test server/Modon.Platform.Tests/Modon.Platform.Tests.csproj

# partner portal
cd apps/portal
npm test
npm run build

# admin portal
cd apps/admin
npm test
npm run typecheck
npm run build
```

## How the pieces fit

```
Partner portal :5173 ─┐
                      ├─/api──► Modon.Platform :5100 ──► PostgreSQL :5433
Admin portal   :5174 ─┘           ├─ FormCMS: login cookie, roles, entities, saved queries
                                  └─ Modon:   personas, requests, attachments, review, seeding
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
| `GET /api/modon/review/summary` | Modon (reviewers) | Counts per status (drafts excluded) |
| `GET /api/modon/review/requests?status=&service=&search=&page=&pageSize=` | Modon (reviewers) | Request queue |
| `GET /api/modon/review/requests/{id}` · `…/{id}/history` | Modon (reviewers) | Full request · audit history (actor, action, comment, time) |
| `GET /api/modon/review/requests/{id}/attachments/{slot}` | Modon (reviewers) | Download a submitted attachment |
| `POST /api/modon/review/requests/{id}/approve` | Modon (reviewers) | `{ "comment": "optional" }` → Approved |
| `POST /api/modon/review/requests/{id}/return` | Modon (reviewers) | `{ "comment": "required" }` → ReturnedForCompletion |
| `POST /api/modon/review/requests/{id}/reject` | Modon (reviewers) | `{ "reason": "required" }` → Rejected |

Security rules enforced by the backend:

- Users only ever see their own requests (other people's requests return 404).
- Only contractors can apply for the excavation permit, and only for an establishment they are linked to.
- Statuses change only through the workflow; every successful change writes exactly one history row
  (who, what, comment, when). Legal transitions today:
  `Draft → InProgress` (applicant submits), `InProgress → Approved | Rejected | ReturnedForCompletion` (reviewer).
  Anything else returns **409**. If two reviewers decide at the same moment, only the first decision is saved
  (the second gets 409).
- Review endpoints require the `modon_reviewer` role (or the FormCMS super admin `sa`). Investors,
  contractors, consultants and the FormCMS `admin` role get **403**; anonymous callers get **401**.
  Reviewers never see drafts.
- Uploaded documents are stored **outside** `wwwroot` (`server/Modon.Platform/App_Data/private-documents`)
  and can only be downloaded by their owner or, once submitted, by a reviewer. Size, extension, MIME type
  and file signature are checked.
- State-changing `/api/modon` calls need the header `X-Modon-Client` (the portal sends it), which blocks cross-site form posts.

The catalogue is a normal FormCMS entity (`modonService`), so its text can be edited later from the
FormCMS admin tools without code changes. The seeder only adds missing services; it never overwrites edits.

## Troubleshooting

| Problem | Fix |
|---|---|
| `Failed to connect to 127.0.0.1:5433` | PostgreSQL isn't running: start Docker Desktop, then `docker compose up -d`. Or use `--launch-profile http-sqlite`. |
| `error during connect … dockerDesktopLinuxEngine` | Docker Desktop isn't started. Open it and wait until it says "running". |
| Port 5100, 5173 or 5174 already in use | Stop the other program, or close an old terminal still running the backend/portal. |
| Portal shows "Could not load data" | The backend isn't running on port 5100. |
| Admin sign-in says "تعذر الاتصال بالخادم" (cannot reach the server) | The backend isn't running on port 5100, or it stopped at startup. Check its terminal for errors. |
| Backend stops at startup with `NpgsqlException: The operation has timed out` | It is connecting to IPv6 `::1`, which Docker Desktop's port forward sometimes accepts but never answers. The dev connection string uses `Host=127.0.0.1` for this reason. Keep it that way, or restart Docker Desktop. |
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

## Milestone 3 assumptions (to confirm with the instructor)

- Any reviewer can see and decide on **every** submitted request; there is no split by industrial city yet.
- The FormCMS super admin (`sa`) may also review; the FormCMS `admin` role may not.
- Decisions are final within this milestone: there is no "undo", and decided requests cannot be decided again.
  A returned request cannot be edited or resubmitted yet (Milestone 4).
- An approval note is optional; return comments and rejection reasons are required (max 2,000 characters).
- A reviewer who also holds an applicant role is not blocked from reviewing their own request
  (no separation-of-duties rule was specified).
- No email/SMS is sent on decisions yet.
