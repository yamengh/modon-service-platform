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

**Current milestones (6–8): Building Licence & Construction Reports** (رخصة البناء وتقارير البناء), following the
building-licence user manual (January 2026) — step-by-step in section 5f:

- the investor's **application**: drafts, commercial registration, contract / registration number, licence type,
  construction budget (SAR), floors, scope of work, notes, contacts, the approved general and safety plans, an optional
  general attachment and the terms — with a `BLR-` request number, status tracking and the full history;
- the **consulting office** stage: an open project (**المشروع**, with a `Bid-` project number) where registered
  consulting offices quote, or a **specific office** that accepts (amount, phase table with durations, commitments,
  optional attachment) or rejects (comment). The investor picks a quotation, or rejects all / cancels the project;
- the **contractor** stage, with the same two modes, ending at the **Modon reviewer** (the industrial-city officer),
  who approves or rejects in the admin portal;
- **monthly construction-progress reports** by the selected consulting office for an approved licence (six phases,
  0–100 % each), visible to the investor and the reviewer;
- a **separate Camunda process `modon-building-licence`** next to the unchanged excavation process (section 7.6).

Milestone 5: the Excavation Works Permit lifecycle can run on a **real Camunda 8.9 process**
(`excavation-permit.bpmn`). Camunda is switched on per run (section 7); without it the backend uses its built-in
(in-process) rules.

Everything from Milestone 4:

- **Partner portal** — sign-in, the service catalogue, and the **Excavation Works Permit** application
  (drafts, the full form, six required attachments, submission and request tracking), plus:
  - a **task inbox** (**المهام**) listing requests that Modon returned or that are waiting for coordination letters;
  - **return → edit → resubmit**: reopen a returned request, save changes, and resubmit it (same request, same number);
  - **coordination letters**: upload, replace or remove the three letters, then send them.
- **Admin portal** — staff sign-in, a dashboard of request counts by status, a filterable request queue,
  full request details (form, applicant, attachments, coordination letters, audit history), and review decisions:
  **approve**, **request coordination letters** (optional instructions), **return for completion**
  (comment required) and **reject** (reason required).

Not in these milestones: the Expo mobile app and e-mail/SMS notifications (the manual mentions an e-mail and SMS
after submission); both belong to the remaining delivery phases.

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
dotnet run                                # PostgreSQL (option A), built-in workflow rules
dotnet run --launch-profile http-camunda  # PostgreSQL + Camunda 8.9 workflow (section 7)
dotnet run --launch-profile http-sqlite   # SQLite (option B)
```

Wait for `Now listening on: http://localhost:5100`. On first start it automatically:

- creates the database tables (FormCMS + user accounts),
- creates the roles `investor`, `contractor`, `consultant`, `modon_reviewer`,
- creates the service catalogue and its two services,
- creates the Modon request tables (PostgreSQL: EF Core migrations; SQLite: created once),
- creates the dropdown options (industrial cities, permit types, …),
- creates development accounts and their establishments, including a registered consulting office and a
  registered contractor for the building licence (Development environment only).

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
| `investor@modon.local` | investor | ✅ — can apply for the Building Licence (establishment CR `7098765432`, مصنع الشرق للصناعات) |
| `contractor@modon.local` | contractor | ✅ — can apply for the Excavation Works Permit (establishment CR `7012345678`) and sees its tasks; the same establishment is a **registered contractor**, so it can quote for and accept building projects (**المشاريع**) |
| `consultant@modon.local` | consultant | ✅ — member of the **registered consulting office** CR `7055501234` (المكتب الهندسي الاستشاري): quotes for, accepts and reports on building projects (**المشاريع**) |
| `reviewer@modon.local` | modon_reviewer | ❌ partner portal · ✅ **admin portal** (review requests) |
| `sadmin@modon.local` | FormCMS super admin | ❌ partner portal · ✅ admin portal (super admin has full data access in FormCMS) |

Roles cannot be self-assigned: new sign-ups get no Modon role, and only a FormCMS super admin can change roles.
Whether an establishment is a registered consulting office or contractor (`modon_establishments.IsConsultingOffice`,
`IsContractor`) is set only by seeding; there is no endpoint for it.

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

## 5f. Building licence: the whole lifecycle (step by step)

Use three browser profiles or private windows (the portals share the login cookie per browser): one for the
investor, one for the consultant / contractor, one for the reviewer.

**Application (manual 1.1) — `investor@modon.local`, partner portal**

1. **خدمات مدن** → **رخصة البناء وتقارير البناء** → **بدء الخدمة**. A draft opens with a number like
   `BLR-000044-19-10-26`.
2. Fill in **السجل التجاري**, **رقم العقد / رقم التسجيل** (e.g. `25000168`), **نوع الرخصة** (ترخيص بناء جديد),
   **ميزانية الإنشاء** (riyals), **عدد الأدوار**; optionally **إطار العمل**, **ملاحظات إضافية** and **جهات الاتصال**
   (name, e-mail, mobile for each person). **حفظ كمسودة** saves without checking required fields.
3. Upload **المخططات العامة المعتمدة الصادرة** and **مخططات السلامة المعتمدة الصادرة** (PDF/PNG); the general
   attachment is optional. Tick **للتحقق من الأحكام والشروط** and press **تقديم الطلب**.
   The request becomes **مرحلة اختيار المكتب الاستشاري** and the form is locked.

**Consulting office — open project (manual 1.2, 1.3, 1.5)**

4. On the request page choose **المشروع** → **التقدم في الطلب**. Status **تقديم عروض السعر من المكاتب**; the page
   shows the project number (`Bid-…`).
5. As `consultant@modon.local`: **المشاريع** → **المشاريع المطروحة** → open the project → choose the office, enter
   **قيمة عرض السعر** and a comment → **تقديم**.
6. As the investor: the request page (and **المهام**) lists the quotations. Pick one → **إختيار عرض سعر** → the
   request moves to **مرحلة اختيار المقاول**. Or **رفض جميع العروض / إلغاء المشروع** → back to step 4.

**Consulting office — specific office (manual 1.4)**

7. Instead of step 4, choose **اختيار مكتب إستشاري محدد**, pick *المكتب الهندسي الاستشاري*, **التقدم في الطلب**.
   Status **معلقة على قرار المكتب الاستشاري**.
8. As `consultant@modon.local`: **المهام** (or **المشاريع**) → the request → **قبول**: fill the phase table
   (share % and days for the six phases), tick the commitments, enter the amount, optionally attach a file →
   **تقديم** → **مرحلة اختيار المقاول**. Or **رفض** with a comment → the investor is back at step 4 and sees the comment.

**Contractor (manual 1.6, 1.7)**

9. As the investor, choose **المشروع** or **اختيار مقاول محدد** (*مؤسسة البناء المتقدم للمقاولات*).
10. As `contractor@modon.local`: quote in **المشاريع المطروحة** (then the investor picks it), or accept / reject the
    direct request (amount required to accept, comment required to reject).
11. When a contractor is chosen the request becomes **دراسة الطلب** (with the industrial-city officer).

**Modon review (manual 1.7.5) — `reviewer@modon.local`, admin portal**

12. Dashboard → **دراسة الطلب** → open the request: basic information, contacts, plans, the chosen consulting office
    and contractor with their quotations, phase plan and attachments, every selection round, and the history.
13. **اعتماد الطلب** (optional note) or **رفض الطلب** (reason required). Both are final.

**Monthly progress reports — `consultant@modon.local`**

14. **المشاريع** → **المشاريع الراسية** → the approved project → **تقرير شهري جديد**: the month (not in the future),
    the completion of each of the six phases (0–100 %), a comment and an optional attachment → **تقديم التقرير**.
    One report per month. The investor and the reviewer see the current completion bars and every report.

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
(`dotnet run`) and follow steps 5b–5f by hand; the request history in the admin portal must show one row
per status change (create, submit, return, resubmit, letters requested, letters sent, approve).

Building-licence tests: `BuildingLicenceTests.cs` (in-process engine: drafts and validation, open project and
specific-office paths, quotation choice and reject-all, contractor stage, review, ownership and office checks, progress
reports, simultaneous actions), `Camunda/CamundaBuildingTests.cs` (the same lifecycle on the building BPMN in the fake
cluster: start once, correlation, rejection loops, not deployed, Camunda down, lost response, conflicting answers) and
`Camunda/BuildingBpmnTests.cs` (the BPMN accepts exactly the transitions of the in-process rules).

The Camunda tests (`server/Modon.Platform.Tests/Camunda/`) need **no** Camunda cluster: they run the real
adapter and Camunda SDK against a fake cluster that follows the checked-in BPMN. Checking against the real
cluster is a separate, manual step — see section 7.5.

## How the pieces fit

```
Partner portal :5173 ─┐
                      ├─/api──► Modon.Platform :5100 ──► PostgreSQL :5433
Admin portal   :5174 ─┘           ├─ FormCMS: login cookie, roles, entities, saved queries
                                  ├─ Modon:   personas, requests, attachments, tasks, review, seeding
                                  │           (own tables "modon_*", EF Core)
                                  └─ Workflow: in-process rules, or ──REST /v2──► Camunda 8.9 :8080
                                                     (processes "modon-excavation-permit", "modon-building-licence")
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
| `GET /api/modon/tasks` | Modon | My task inbox: excavation requests returned or awaiting letters; building requests waiting for my choice; building requests where an investor chose my office |
| `PUT /api/modon/requests/{id}/building` | Modon (investor) | Save the building-licence draft (format checks only) |
| `POST /api/modon/requests/{id}/building/{consultant\|contractor}/selection` | Modon (investor) | `{ "mode": "openTender" \| "direct", "establishmentId": … }` — open project or specific office |
| `POST /api/modon/requests/{id}/building/{stage}/quotations/{qid}/accept` | Modon (investor) | Pick a quotation of the open project |
| `POST /api/modon/requests/{id}/building/{stage}/quotations/reject-all` | Modon (investor) | `{ "comment": "optional" }` — reject all / cancel the project |
| `GET /api/modon/requests/{id}/building/{quotations\|progress-reports}/{fid}/attachment` | Modon (investor) | Download an office's or a report's attachment |
| `GET /api/modon/building/offices?stage=consultant\|contractor` | Modon | Registered consulting offices / contractors (for a direct choice) |
| `GET /api/modon/building/projects` · `GET …/projects/{id}` | Modon (offices) | My open projects, invitations and awarded projects · one project (no contacts; plans only once invited or selected) |
| `POST /api/modon/building/projects/{id}/quotations` | Modon (offices) | `{ "establishmentId", "amountSar", "comment" }` — quote for an open project |
| `POST /api/modon/building/projects/{id}/response` | Modon (offices) | Accept / reject a direct request: JSON, or multipart (`data` = JSON, `file`) |
| `POST /api/modon/building/projects/{id}/progress-reports` | Modon (selected consulting office) | `{ "period": "yyyy-MM", "phases": [...], "comment" }`, JSON or multipart with `file` |
| `GET /api/modon/review/requests/{id}/building/{quotations\|progress-reports}/{fid}/attachment` | Modon (reviewers) | Download an office's or a report's attachment |
| `GET /api/modon/review/summary` | Modon (reviewers) | Counts per status (drafts excluded) |
| `GET /api/modon/review/requests?status=&service=&search=&page=&pageSize=` | Modon (reviewers) | Request queue |
| `GET /api/modon/review/requests/{id}` · `…/{id}/history` | Modon (reviewers) | Full request · audit history (actor, action, comment, time) |
| `GET /api/modon/review/requests/{id}/attachments/{slot}` | Modon (reviewers) | Download a submitted attachment |
| `POST /api/modon/review/requests/{id}/approve` | Modon (reviewers) | `{ "comment": "optional" }` → Approved |
| `POST /api/modon/review/requests/{id}/return` | Modon (reviewers) | `{ "comment": "required" }` → ReturnedForCompletion |
| `POST /api/modon/review/requests/{id}/reject` | Modon (reviewers) | `{ "reason": "required" }` → Rejected |
| `POST /api/modon/review/requests/{id}/request-coordination-letters` | Modon (reviewers) | `{ "comment": "optional" }` → AwaitingCoordinationLetters |
| `GET /api/modon/review/workflow` | Modon (reviewers) | Which workflow engine is configured and whether it works **now** (Camunda reachable, partitions healthy, process deployed) |
| `GET /api/modon/review/requests/{id}/workflow` | Modon (reviewers) | The request's PostgreSQL status next to where its Camunda process instance is (`inSync`) |
| `GET /health` | Modon (anyone) | `Healthy` / `Unhealthy` (503) for the configured workflow engine — no details |

Security rules enforced by the backend:

- Users only ever see their own requests (other people's requests return 404).
- Only contractors can apply for the excavation permit, and only investors for the building licence — each only for
  an establishment they are linked to.
- Building licence offices: quoting, answering and reporting need the consultant (or contractor) role **and**
  membership of an establishment registered as a consulting office (or contractor). An office sees an open project of
  its kind, a request where it was chosen directly, or a project it was selected for — nothing else (404). An office
  quotes once per round, never on its own investor's request, and never sees other offices' quotations. Only the
  selected consulting office reports, only for an approved licence.
- Statuses change only through the workflow; every successful change writes exactly one history row
  (who, what, comment, when). Legal transitions today:
  - applicant: `Draft → InProgress` (submit), `ReturnedForCompletion → InProgress` (resubmit),
    `AwaitingCoordinationLetters → InProgress` (send letters);
  - reviewer: `InProgress → Approved | Rejected | ReturnedForCompletion | AwaitingCoordinationLetters`.
  - building licence: `Draft → ConsultantSelection` (submit); `ConsultantSelection → ConsultantTender | AwaitingConsultantDecision`;
    `ConsultantTender → ContractorSelection` (quotation chosen) `| ConsultantSelection` (all rejected);
    `AwaitingConsultantDecision → ContractorSelection` (office accepts) `| ConsultantSelection` (office rejects);
    the contractor stage the same way, ending in `UnderModonReview → Approved | Rejected` (reviewer);
    `Approved → Approved` for each monthly progress report (a history row, no status change).

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
- With Camunda enabled, every status change is first performed in Camunda and then saved in PostgreSQL. If Camunda
  is unreachable, refuses the call or the process is not deployed, the action returns **503** `workflowUnavailable`
  and nothing changes. See section 7.4 for how duplicates and failures are handled.

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
| Startup log says `Workflow engine NOT ready (camunda): Camunda is NOT reachable` | Camunda isn't running or the address is wrong. Start it (section 7.1) and check `Modon:Workflow:Camunda:RestAddress`. Use `127.0.0.1`, not `localhost` (the same Docker IPv6 problem as PostgreSQL). |
| Startup log says `Process 'modon-excavation-permit' is not deployed` | Deploy the BPMN (section 7.2), then restart the backend or reopen the admin dashboard. |
| Submitting or deciding shows "خدمة سير العمل … غير متاحة" / "تعذر الوصول إلى محرك سير العمل" (HTTP 503) | Camunda was not reachable for that call. Nothing was changed; fix Camunda and try again. Details: admin dashboard → محرك سير العمل, or the backend log. |
| A decision fails with "هذا الطلب غير مرتبط بعملية في Camunda" (409 `workflowNotStarted`) | The request was submitted while Camunda was off, so it has no process instance. Handle it with the built-in engine (`dotnet run` without `http-camunda`). |
| Startup log says `Process 'modon-building-licence' is not deployed` | Deploy `building-licence.bpmn` (section 7.2). Until then building submissions and stage actions get 503; the excavation permit keeps working. |
| A consultant or contractor sees no projects | The user needs the role **and** membership of an establishment flagged `IsConsultingOffice` / `IsContractor` (dev seeding does this for the demo accounts). Open projects only appear after the investor chose **المشروع**. |
| The investor sees no quotations yet | Quotations appear when an office submits one; the task **اختيار عرض سعر من المكاتب** appears with the first one. |
| Camunda element names look garbled in Operate (e.g. `Ã˜Â…`) | The local bundle mis-reads non-ASCII names. The checked-in BPMN uses English names only; don't add Arabic names to it. |

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

## 7. Camunda 8.9 workflow (Milestones 5–8)

The excavation lifecycle and (since Milestone 6) the building-licence lifecycle can run on real BPMN processes in
Camunda — one process per service (section 7.6 for the building licence). Modon.Platform talks to the cluster's
**Orchestration Cluster REST API** (`/v2`) through Camunda's C# SDK, **`Camunda.Orchestration.Sdk` 9.2.2** —
the SDK line that matches Camunda **8.9** (SDK major 9 = server 8.9). Camunda only appears in Modon.Platform
(`server/Modon.Platform/Requests/Workflow/Camunda/`); nothing in FormCMS changed.

**The process** — `server/Modon.Platform/Requests/Workflow/Camunda/excavation-permit.bpmn`, process id
`modon-excavation-permit`, version tag `1.0.0`:

- **Request submitted** → **Modon review** (an event-based gateway: the first message wins):
  - `ExcavationApproved` → end **Permit approved**
  - `ExcavationRejected` → end **Permit rejected**
  - `ExcavationReturnedForCompletion` → wait at **Contractor resubmits** (`ExcavationResubmitted`) → back to **Modon review**
  - `ExcavationCoordinationLettersRequested` → wait at **Contractor sends the letters**
    (`ExcavationCoordinationLettersSubmitted`) → back to **Modon review**

- The **first submission** starts one process instance. Its key and a correlation key unique to the request are
  stored on the request (`modon_service_requests.WorkflowInstanceKey`, `WorkflowCorrelationKey`, definition key
  and version). The request number is also the instance's **business id**, so you can find it in Operate.
- Every later action — approve, reject, return, request letters, resubmit, send letters — is a **message**
  correlated to that same instance with the stored correlation key. No second instance is ever started for a request.

### 7.1 The local Camunda cluster

The project uses your existing Camunda 8.9 Docker bundle (`C:\Users\yamen\Downloads\docker-compose-8.9`,
the lightweight `docker-compose.yaml`: one `orchestration` container, version 8.9.21, H2 storage). Settings found
in that bundle and confirmed against the running cluster:

| What | Value |
|---|---|
| REST API (used by Modon) | `http://127.0.0.1:8080` (Modon adds `/v2`) |
| gRPC (Modeler, zbctl) | `127.0.0.1:26500` |
| Operate / Tasklist | <http://127.0.0.1:8080/operate>, <http://127.0.0.1:8080/tasklist> — sign in `demo` / `demo` |
| Authentication | `basic`, with `unprotectedApi: true` → API calls work **without credentials** (auth strategy `NONE`). `BASIC` with `demo`/`demo` also works. |
| Authorizations, multi-tenancy | off |

Start / stop only the orchestration container (Modon does not need `connectors`):

```bash
docker start orchestration     # wait until "docker ps" shows it as healthy (about 30 s)
docker stop orchestration
```

Check it: `curl http://127.0.0.1:8080/v2/topology` must show `"gatewayVersion":"8.9.21"` and a `healthy` partition.

### 7.2 Deploy the BPMN

Each process must be deployed **once** (and again after any change to its BPMN). Modon never deploys anything by
itself. There are two files, deployed the same way:

- `server/Modon.Platform/Requests/Workflow/Camunda/excavation-permit.bpmn` (process `modon-excavation-permit`)
- `server/Modon.Platform/Requests/Workflow/Camunda/building-licence.bpmn` (process `modon-building-licence`, Milestone 6)

**With Camunda Modeler (desktop):**

1. Open Camunda Modeler → **File → Open File…** → `server/Modon.Platform/Requests/Workflow/Camunda/excavation-permit.bpmn`.
   The diagram opens as a Camunda 8 diagram (execution platform 8.9).
2. Click the **rocket** icon (*Deploy current diagram*) in the bottom-left toolbar.
3. Choose **Camunda 8 Self-Managed**.
4. **Cluster endpoint**: `http://127.0.0.1:26500` (the gRPC gateway of the `orchestration` container).
   Use `127.0.0.1`, not `localhost`.
5. **Authentication**: **None** (the local API is unprotected). If your Modeler version insists, choose
   **Basic** with `demo` / `demo`.
6. Click **Deploy**. Modeler shows "Diagram deployed" with a process definition version.

> These Modeler steps follow Camunda Modeler 5's deploy dialog and the endpoints of this cluster, but they were
> **not run during Milestone 5** (no desktop UI was available). The deployment that was actually performed and
> verified is the command-line one below. If your Modeler version asks for the REST address instead, use
> `http://127.0.0.1:8080`.

**Or from the command line** (the same deployment through the REST API — this is how it was deployed and
checked during Milestone 5):

```bash
curl -X POST http://127.0.0.1:8080/v2/deployments \
  -F "resources=@server/Modon.Platform/Requests/Workflow/Camunda/excavation-permit.bpmn"
curl -X POST http://127.0.0.1:8080/v2/deployments \
  -F "resources=@server/Modon.Platform/Requests/Workflow/Camunda/building-licence.bpmn"
```

The building-licence command was run during Milestones 6–8; the local cluster has `modon-building-licence`
version 1 (key `2251799813725821`). The Modeler steps above apply unchanged to `building-licence.bpmn`.

**Verify the deployment** (do not assume it worked):

- Operate → **Processes** → *Modon Excavation Works Permit*, or
- `curl -X POST http://127.0.0.1:8080/v2/process-definitions/search -H "Content-Type: application/json" -d "{\"filter\":{\"processDefinitionId\":\"modon-excavation-permit\"}}"`, or
- the same search with `modon-building-licence`, or
- the admin portal dashboard: **محرك سير العمل: Camunda — جاهز** with `modon-excavation-permit vN` and
  `modon-building-licence vN` (a missing one is shown as *غير منشورة*, and the engine as not ready).

Deploying an unchanged file again does not create a new version. During Milestone 5 the local cluster got
version 1 (with Arabic element names, which the bundle garbled) and version 2 (English names, current).

### 7.3 Run Modon with Camunda

Settings (`server/Modon.Platform/appsettings.Development.json`, section `Modon:Workflow`):

| Setting | Development value | Meaning |
|---|---|---|
| `Engine` | `InProcess` | `InProcess` (built-in rules, used by the automated tests) or `Camunda` |
| `Camunda:RestAddress` | `http://127.0.0.1:8080` | Cluster REST address, without `/v2` |
| `Camunda:AuthStrategy` | `NONE` | `NONE` or `BASIC` (then also `BasicUsername`, `BasicPassword`) |
| `Camunda:ProcessId` | `modon-excavation-permit` | BPMN process id of the excavation permit |
| `Camunda:BuildingLicenceProcessId` | `modon-building-licence` | BPMN process id of the building licence (must differ) |
| `Camunda:TenantId` | (empty) | Only when multi-tenancy is on |
| `Camunda:RequestTimeoutSeconds` | `10` | Per call; a slower answer is treated as "unavailable" |

Start the backend with Camunda switched on (the profile sets `Modon__Workflow__Engine=Camunda`):

```bash
cd server/Modon.Platform
dotnet run --launch-profile http-camunda
```

Invalid settings stop the backend at startup with a readable message. A **reachable** Camunda is not assumed:
about a second after start the backend checks the cluster and logs one of

```
Workflow engine ready (camunda): Camunda 8.9.21 at http://127.0.0.1:8080: process 'modon-excavation-permit' version 2 and process 'modon-building-licence' version 1 are deployed.
Workflow engine NOT ready (camunda): Camunda is NOT reachable: Cannot reach Camunda at … Submitting and reviewing requests will fail with 503 …
Workflow engine NOT ready (camunda): Camunda 8.9.21 at …: Process '…' is not deployed. Deploy excavation-permit.bpmn (see MODON.md). …
```

The same check is on the admin dashboard (**محرك سير العمل**), at `GET /api/modon/review/workflow`, and at
`GET /health`. On a request page the **مقارنة الحالة مع Camunda** button compares PostgreSQL with Camunda.

### 7.4 Consistency rules

- **Camunda first, PostgreSQL second.** An action is checked against the same rules as before (illegal → 409,
  Camunda is not called), then performed in Camunda, then saved in PostgreSQL with one history row.
- **One outcome.** Camunda's event-based gateway accepts only the first decision message; a second, conflicting
  or repeated message finds no waiting instance and is refused (409). PostgreSQL's status concurrency token is the
  second safety net. Both were checked with simultaneous requests against the real cluster.
- **Camunda unavailable** (unreachable, timeout, 4xx/5xx, not deployed): 503, nothing saved. The SDK's automatic
  retry is turned off, so a call is never repeated behind Modon's back.
- **Saving failed after Camunda accepted a message** (e.g. the response was lost): the next attempt of the same
  action finds Camunda already at that step and only saves it — still one history row.
- **Saving failed after a process was started**: the new instance is cancelled. If even the "start" response was
  lost, the left-over instance has a correlation key nobody uses, so it can never receive this request's messages
  (it stays visible in Operate and can be cancelled there).
- Requests submitted while Camunda was off have no instance; with Camunda on they are refused with 409
  `workflowNotStarted` (they are not moved into Camunda automatically).

### 7.5 Check it against the real cluster

1. `docker start orchestration`, deploy the BPMN (7.2), `dotnet run --launch-profile http-camunda`, start both portals.
2. Admin dashboard: **محرك سير العمل: Camunda — جاهز**, version 8.9.21, `modon-excavation-permit v2`.
3. Submit a request as `contractor@modon.local`. In Operate, find the instance by its business id (the request
   number): it waits at **Modon review**. On the request page in the admin portal, **مقارنة الحالة مع Camunda**
   shows *متطابق*.
4. Return it → Operate shows it waiting at **Contractor resubmits**; resubmit → back at **Modon review** (same instance).
5. Request coordination letters → **Contractor sends the letters**; upload and send the three letters → **Modon review**.
6. Approve → the instance is completed at **Permit approved**; the request's history has one row per step.
7. `docker stop orchestration`, try to submit another request → "خدمة سير العمل … غير متاحة", the request stays a
   draft, and the dashboard says Camunda is NOT reachable. Start it again and submit — it works.

### 7.6 The building-licence process (Milestones 6–8)

`server/Modon.Platform/Requests/Workflow/Camunda/building-licence.bpmn`, process id `modon-building-licence`, version
tag `1.0.0` — a separate process; the excavation BPMN and its ids are unchanged. Contract in `BuildingLicenceProcess.cs`.

- **Basic information submitted** (start; Modon starts the instance) → waits at **Investor chooses**
  (`Gateway_ConsultantSelection`):
  - `BuildingConsultantTenderOpened` → **Offices quote** (`Gateway_ConsultantTender`):
    `BuildingConsultantQuotationAccepted` → contractor selection, `BuildingConsultantQuotationsRejected` → back to choosing;
  - `BuildingConsultantSelected` → **Office decides** (`Gateway_ConsultantDecision`):
    `BuildingConsultantAccepted` → contractor selection, `BuildingConsultantDeclined` → back to choosing.
- **Contractor selection** (`Gateway_ContractorSelection`) has the same shape with `BuildingContractor…` messages and
  leads to **Modon review** (`Gateway_LicenceReview`): `BuildingLicenceApproved` or `BuildingLicenceRejected` (end).
- After approval the instance waits at **Monthly progress report** (`Event_AwaitProgressReport`); each report is one
  `BuildingProgressReportSubmitted` message, after which it waits for the next one. Construction completion is not
  described in the manual, so an approved instance stays active (cancel it in Operate when a project is archived).
- Quotations of an open project are **data in PostgreSQL**, not messages: the process only waits for the investor's
  choice. All human work happens in the Modon portals; there are no user tasks and Tasklist is not used.
- The consistency rules of 7.4 apply unchanged (start once at submission, one correlation key per request, Camunda
  first, one outcome per wait state, 503 when Camunda is unavailable, recovery after a lost response).

**Check it against the real cluster:** deploy `building-licence.bpmn` (7.2), `dotnet run --launch-profile http-camunda`,
then follow section 5f. In Operate, search by the business id (the `BLR-` number): after each step the instance waits
at the matching gateway, and **مقارنة الحالة مع Camunda** in the admin portal shows *متطابق*.

## Milestones 6–8 assumptions (building licence; to confirm with the instructor)

Where the manual is silent or contradicts itself, the prototype does the following:

- **Statuses.** The manual's screenshots show *مسودة* during consultant selection, and its text says the status becomes
  *دراسة الطلب* after "التقدم في الطلب", while its screenshots show *تقديم عرض السعر من المكاتب* / *معلقة على قرار المكتب
  الاستشاري* there. The prototype uses the screenshot labels for the stage statuses, records the basic-information
  submission as its own status (**مرحلة اختيار المكتب الاستشاري**) so the application is validated, locked and the
  workflow started exactly once, and uses **دراسة الطلب** for the time with the Modon reviewer.
- After an office accepts, the manual says the request is *قيد إرفاق رخص البناء العامة* ("pending the general building
  licences") without saying who attaches what. No extra upload step was invented: the request goes to contractor
  selection, and the reviewer reviews the plans uploaded with the application plus the offices' attachments.
- **Licence types:** only *ترخيص بناء جديد* is documented, so it is the only option.
- **Contract / registration number** is a free text field (digits, Latin letters, `-`, `/`; max 30): the manual shows a
  dropdown filled from Modon's contract records, which this prototype does not have.
- **Required fields** follow the manual's asterisks: CR, contract number, licence type, budget, floors; scope of work,
  notes and contacts are optional, but each contact row needs name, e-mail and mobile. The two plans are required at
  submission (the catalogue lists them as required documents; the manual's upload box says "minimum 0 files").
- **Uploads:** one file per slot (the manual allows up to five), PDF/PNG plans limited to the platform's 20,480 KB (the
  manual says 1,000,000 KB); the general attachment and the offices' attachments: 10,240 KB,
  JPEG/JPG/PNG/PDF/Doc/Docx/XLS/XLSX (content signature checked).
- **Cancel:** the manual's button is "رفض جميع العروض / إلغاء المشروع" and its text says "إلغاء الطلب". The prototype
  treats both as cancelling the open project (back to choosing); cancelling the whole request is not implemented.
- **Contractor direct rejection** returns to contractor selection (by analogy with the consulting office; the manual
  does not describe it). Rejecting all contractor quotations works the same way.
- **Phase table:** only the directly chosen consulting office fills it in (manual 1.4); quotations in an open project
  have amount and comment only (manual 1.3). Shares are 0–100 % each and are **not** required to add up to 100 % —
  the manual does not say so and its own example adds up to 116 %. Durations are whole days (1–3,650).
- **Commitments** (تعهدات المكتب الاستشاري) are a required checkbox; their text is not in the manual.
- **Modon review:** the manual only says the officer "takes the required decision", so the reviewer can approve or
  reject (reason required); return-for-completion and coordination letters are excavation-only. No licence document is
  issued — approval records the decision.
- **Progress reports:** the manual only says percentages are updated monthly from follow-up reports. The prototype asks
  for the month (`yyyy-MM`, not in the future and not before the approval month, one report per month), all six
  phases' cumulative completion (0–100 %, two decimals), an optional comment and an optional attachment. Only the
  selected consulting office reports; nobody approves reports (no rule documented). Reports do not change the status
  and are not required to increase.
- Offices are establishments flagged as consulting office / contractor by seeding; there is no registration screen for
  offices yet. Open projects are offered to every registered office of the right kind.
- No e-mail or SMS is sent (the manual mentions both after submission).

## Milestone 5 assumptions and limitations

- Camunda is opt-in per run (`http-camunda` profile); the default and the automated tests use the in-process engine.
- PostgreSQL stays the source of truth for what the portals show; Camunda decides which step is possible next.
  There are no job workers or user tasks in Camunda: Modon's own screens do the human work, and Tasklist is not used.
- Requests submitted with the in-process engine are not migrated into Camunda (409 `workflowNotStarted`).
- Process instance creation is not deduplicated by Camunda's business id: the local cluster does not enforce
  business-id uniqueness (checked live), so Modon guards against duplicates itself (section 7.4).
- Only `NONE` and `BASIC` authentication are wired (what the local bundle uses). OAuth/OIDC for a Keycloak-based
  cluster would need a small addition.
- The BPMN has English element names only, because the local bundle garbles non-ASCII names.
- After a lost "start" response an orphan instance may remain in Camunda (harmless, cancel it in Operate).
