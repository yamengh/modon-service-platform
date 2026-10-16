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

**Current milestone (4):** every Excavation Works Permit path in the manual works end to end.

- **Partner portal** — sign-in, the service catalogue, and the **Excavation Works Permit** application
  (drafts, the full form, six required attachments, submission and request tracking), plus:
  - a **task inbox** (**المهام**) listing requests that Modon returned or that are waiting for coordination letters;
  - **return → edit → resubmit**: reopen a returned request, save changes, and resubmit it (same request, same number);
  - **coordination letters**: upload, replace or remove the three letters, then send them.
- **Admin portal** — staff sign-in, a dashboard of request counts by status, a filterable request queue,
  full request details (form, applicant, attachments, coordination letters, audit history), and review decisions:
  **approve**, **request coordination letters** (optional instructions), **return for completion**
  (comment required) and **reject** (reason required).

Not in this milestone: the Building Licence application, the Expo mobile app and Camunda (each has its own
milestone), and email/SMS notifications.

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
| `contractor@modon.local` | contractor | ✅ — can apply for the Excavation Works Permit (establishment CR `7012345678`) and sees its tasks |
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
5. Press **تقديم الطلب** (submit). The request becomes **قيد التنفيذ** (in progress) and can no longer be
   edited — unless Modon returns it for completion (step 5d).
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
   A fourth decision, **طلب خطابات التنسيق** (request coordination letters, optional instructions), asks the
   contractor for the three coordination letters (step 5e).

## 5d. Complete a returned request (partner portal)

1. In the admin portal, open an in-progress request and choose **إعادة لإكمال النواقص** with a comment.
2. Sign in to the partner portal as `contractor@modon.local` and open **المهام** (tasks). The request is listed
   with its status **إكمال النواقص**, the reviewer's comment and the date.
3. Press **إكمال النواقص وإعادة التقديم**. The original form opens with the reviewer's instructions on top.
   Everything can be changed except the establishment (it is fixed once a request has been submitted).
4. **حفظ التعديلات** saves your changes without resubmitting — the request stays in your tasks.
5. **إعادة تقديم الطلب** checks every required field and all six attachments again, then sends the request back
   to Modon as **قيد التنفيذ**. It keeps its number; the history shows the return and the resubmission.

## 5e. Send coordination letters (partner portal)

1. In the admin portal, open an in-progress request and choose **طلب خطابات التنسيق** (instructions optional).
   The request becomes **بانتظار خطابات التنسيق**, and no other decision is possible until the letters arrive.
2. As `contractor@modon.local`, open **المهام** → **رفع خطابات التنسيق**.
3. Upload the three letters — Saudi Electricity Company, National Grid (الشركة الوطنية لنقل الكهرباء) and the
   East electricity service office — one file each, max 20,480 KB, JPEG/JPG/PNG/PDF. Each file is saved as soon
   as it is uploaded, so you can replace or remove it and come back later.
4. **إرسال خطابات التنسيق** requires all three. The request returns to **قيد التنفيذ**, the letters are locked,
   and the reviewer sees them under **خطابات التنسيق** in the admin portal.

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

The backend tests run on SQLite. To check the same flows against PostgreSQL, start the backend normally
(`dotnet run`) and follow steps 5b–5e by hand; the request history in the admin portal must show one row
per status change (create, submit, return, resubmit, letters requested, letters sent, approve).

## How the pieces fit

```
Partner portal :5173 ─┐
                      ├─/api──► Modon.Platform :5100 ──► PostgreSQL :5433
Admin portal   :5174 ─┘           ├─ FormCMS: login cookie, roles, entities, saved queries
                                  └─ Modon:   personas, requests, attachments, tasks, review, seeding
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
| `POST /api/modon/requests/{id}/submit` | Modon | Submit a draft, or resubmit a returned request (all required fields and attachments) |
| `POST · DELETE · GET /api/modon/requests/{id}/attachments/{slot}` | Modon | Upload (multipart `file`) · remove · download — application documents and coordination letters |
| `POST /api/modon/requests/{id}/coordination-letters/submit` | Modon | Send the three coordination letters → InProgress |
| `GET /api/modon/tasks` | Modon | My task inbox: my requests returned for completion or awaiting coordination letters |
| `GET /api/modon/review/summary` | Modon (reviewers) | Counts per status (drafts excluded) |
| `GET /api/modon/review/requests?status=&service=&search=&page=&pageSize=` | Modon (reviewers) | Request queue |
| `GET /api/modon/review/requests/{id}` · `…/{id}/history` | Modon (reviewers) | Full request · audit history (actor, action, comment, time) |
| `GET /api/modon/review/requests/{id}/attachments/{slot}` | Modon (reviewers) | Download a submitted attachment |
| `POST /api/modon/review/requests/{id}/approve` | Modon (reviewers) | `{ "comment": "optional" }` → Approved |
| `POST /api/modon/review/requests/{id}/return` | Modon (reviewers) | `{ "comment": "required" }` → ReturnedForCompletion |
| `POST /api/modon/review/requests/{id}/reject` | Modon (reviewers) | `{ "reason": "required" }` → Rejected |
| `POST /api/modon/review/requests/{id}/request-coordination-letters` | Modon (reviewers) | `{ "comment": "optional" }` → AwaitingCoordinationLetters |

Security rules enforced by the backend:

- Users only ever see their own requests (other people's requests return 404).
- Only contractors can apply for the excavation permit, and only for an establishment they are linked to.
- Statuses change only through the workflow; every successful change writes exactly one history row
  (who, what, comment, when). Legal transitions today:
  - applicant: `Draft → InProgress` (submit), `ReturnedForCompletion → InProgress` (resubmit),
    `AwaitingCoordinationLetters → InProgress` (send letters);
  - reviewer: `InProgress → Approved | Rejected | ReturnedForCompletion | AwaitingCoordinationLetters`.

  Anything else returns **409**; Approved and Rejected are final. If two people act on the same request at the
  same moment (two reviewers, or a double-clicked resubmit), only the first action is saved and the other gets 409.
- The applicant may edit the form and its six attachments only in **Draft** or **ReturnedForCompletion**, and
  upload coordination letters only in **AwaitingCoordinationLetters**; otherwise uploads, edits and submissions get 409.
- After the first submission a request belongs to its establishment: the applicant must still be a member of it to
  see it in the task inbox or act on it (**403** otherwise), and the establishment cannot be changed.
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
- Approve and reject are final: there is no "undo", and decided requests cannot be decided again.
  (Returning a request for completion is not final — see Milestone 4.)
- An approval note is optional; return comments and rejection reasons are required (max 2,000 characters).
- A reviewer who also holds an applicant role is not blocked from reviewing their own request
  (no separation-of-duties rule was specified).
- No email/SMS is sent on decisions yet.

## Milestone 4 assumptions (to confirm with the instructor)

- Tasks belong to the contractor who created the request, and only while they are still a member of the
  request's establishment. Colleagues in the same establishment do not share requests or tasks (no delegation
  rule was specified).
- The establishment of a request cannot change after its first submission.
- The original submission date is kept when a request is resubmitted; the resubmission time is in the history.
- Saving changes to a returned request (or uploading a letter) is not a status change, so it is not a history row;
  each attachment records who uploaded it and when.
- Coordination letters can be requested only for an in-progress request, and again later if needed (the
  contractor can then replace the earlier letters). While a request is returned or awaiting letters, reviewers
  have no decision available until the contractor responds.
- Approval does not require coordination letters first: the manual does not describe the final decision.
- Instructions are optional when requesting letters (the three letters are fixed); a return still needs a comment.
- The letter names follow the catalogue's required-documents text: الشركة السعودية للكهرباء،
  الشركة الوطنية لنقل الكهرباء، مكتب خدمة كهرباء شرق.
- No email/SMS is sent when a request is returned, when letters are requested, or when the contractor responds.
