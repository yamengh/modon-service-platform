# Modon Service Platform

A prototype e-services platform for **Modon** (the Saudi Authority for Industrial Cities and Technology Zones). Investors,
contractors and consulting offices apply for services and follow their requests on the web or on a phone, and Modon
reviewers decide on them in a separate admin portal. Every status change is recorded, checked by the backend and can
be driven by a Camunda 8.9 BPMN process.

The platform is built on the open-source **[FormCMS](https://github.com/formcms/formcms)** foundation (see
[Attribution](#attribution-and-licence)). Detailed setup, step-by-step walkthroughs and design notes are in
**[MODON.md](MODON.md)**.

## Implemented services

### Building Licence & Construction Reports (رخصة البناء وتقارير البناء)

- **Investor application:** drafts, commercial registration, contract number, licence type, budget, floors, contacts,
  approved general and safety plans, terms; request numbers like `BLR-000044-19-10-26`.
- **Consulting-office stage:** either an open project (`Bid-…`) where registered consulting offices submit quotations
  and the investor picks one (or rejects all), or a specific office that accepts (amount, six-phase plan, commitments,
  optional attachment) or rejects with a comment.
- **Contractor stage:** the same two modes.
- **Modon review:** the reviewer sees the plans, offices, quotations and history, then approves or rejects.
- **Monthly construction-progress reports** by the selected consulting office (six phases, 0–100 % each), visible to the
  investor and the reviewer.

### Excavation Works Permit (تصريح أعمال الحفر)

- **Contractor application:** drafts, the full form and six required attachments; request numbers like
  `EXC-000001-15-10-26`.
- **Modon review:** approve, reject (reason required), return for completion (comment required) or request coordination
  letters.
- **Returned requests:** the contractor edits and resubmits (same request, same number).
- **Coordination letters:** the contractor uploads the three letters (Saudi Electricity Company, National Grid, East
  electricity service office) and sends them back for review.

## Features by role

| Role | Where | What they can do |
|---|---|---|
| Investor | Partner portal, mobile app | Building licence application, choose consulting office and contractor (open project or specific office), compare and pick quotations, follow progress reports, task inbox, notifications |
| Contractor | Partner portal, mobile app | Excavation permit application, resubmission and coordination letters; as a registered contractor: quote for, accept or reject building projects |
| Consulting office | Partner portal, mobile app | Quote for open projects, accept or reject direct requests (phase plan, commitments), submit monthly progress reports |
| Modon reviewer | Admin portal (web only) | Dashboard of counts by status, filterable request queue, full request details and audit history, document downloads, decisions for both services, workflow-engine status |

The backend enforces all rules:
- Users see only their own requests.
- Each service is limited to its persona and linked establishment.
- Offices see only the projects they may act on.
- Statuses change only through legal transitions; anything else gets HTTP 409.
- Every change writes exactly one history row.

## Architecture

```
Partner portal :5173 ─┐   (cookie)
Admin portal   :5174 ─┼─/api──► Modon.Platform :5100 ──► PostgreSQL :5433
Mobile app (Expo)    ─┘   (bearer token)
                                  ├─ FormCMS: login cookie, roles, entities, saved queries, notification inbox
                                  ├─ Modon:   personas, requests, attachments, tasks, review, notifications
                                  └─ Workflow: in-process rules, or ──REST /v2──► Camunda 8.9 :8080
```

| Layer | Technology | Folder |
|---|---|---|
| Backend | ASP.NET Core on .NET 10, EF Core (own `modon_*` tables, migrations), ASP.NET Identity via FormCMS | `server/Modon.Platform/` |
| Workflow | Built-in rules, or Camunda 8.9 through `Camunda.Orchestration.Sdk` 9.2.2: one BPMN process per service (`modon-excavation-permit`, `modon-building-licence`) | `server/Modon.Platform/Requests/Workflow/` |
| Database | PostgreSQL 16 (Docker Compose); SQLite as a no-Docker option and for tests | `docker-compose.yml` |
| Partner web portal | React 19, Vite 6, TypeScript, React Router; Arabic-first (RTL) with English | `apps/portal/` |
| Admin (reviewer) portal | Same stack | `apps/admin/` |
| Mobile app | Expo SDK 58, React Native 0.88, Expo Router, TypeScript; Arabic/English | `apps/mobile/` |
| Backend tests | xUnit with the real backend on a temporary SQLite database and a fake Camunda cluster | `server/Modon.Platform.Tests/` |

Security highlights:
- **Private documents:**
  - stored outside the web root and never given a public URL;
  - downloadable only by their owner, or by a reviewer once submitted;
  - checked for size, extension, MIME type and file signature.
- **Mobile sign-in:** the app uses its own user-specific bearer token. Only its SHA-256 hash is stored on the server, and
  on the phone it is kept in `expo-secure-store`. It never uses FormCMS's shared API key.
- **Roles cannot be self-assigned.**

## Notifications

- **In-app notifications are implemented.** Each workflow step notifies the right applicant or office, exactly once and
  only to its recipient. They appear in the partner portal (**الإشعارات**, with an unread count) and in the mobile app's
  notifications tab, and are copied into FormCMS's own notification inbox.
- **E-mail and SMS are logging stubs.** They only write `[DEV E-MAIL — NOT SENT]` / `[DEV SMS — NOT SENT]` lines to the
  backend log. No message is sent and no provider is configured.
- **Mobile push notifications are not implemented.** The app refreshes its badge and lists while it is open.

## Quick start

Requirements: .NET SDK 10, Node.js 22, Docker Desktop. Run each part in its own terminal from the repository root:

```bash
docker compose up -d                          # PostgreSQL on 127.0.0.1:5433

cd server/Modon.Platform
dotnet run                                    # backend on http://localhost:5100 (built-in workflow rules)
# dotnet run --launch-profile http-camunda    # same, using a local Camunda 8.9 cluster (MODON.md §7)

cd apps/portal && npm install && npm run dev  # partner portal  http://localhost:5173
cd apps/admin  && npm install && npm run dev  # admin portal    http://localhost:5174
```

On first start in the Development environment, the backend creates the tables, roles, the service catalogue, lookup
values and the demo accounts.

**Mobile app:**
1. Copy `apps/mobile/.env.example` to `.env.local`.
2. Set `EXPO_PUBLIC_API_BASE_URL` to an address the phone can reach. For a phone on the same Wi-Fi this is
   `http://<PC LAN IP>:5100`, with the backend started using `--launch-profile http-lan`.
3. Run `npx expo start` in `apps/mobile` and open the app in Expo Go.

See MODON.md §8.3 for the emulator and LAN details.

More detail in MODON.md:
- §1–4: setup
- §5b–5f: step-by-step walkthroughs of both services
- §7: Camunda (deploying the BPMN)
- §10: an end-to-end test script
- Troubleshooting section

## Demo accounts (development only)

All accounts use the password **`Modon@Dev1`**. They are seeded only in the Development environment and must never be
used in production.

| Account | Role | Use |
|---|---|---|
| `investor@modon.local` | Investor | Building licence applicant |
| `contractor@modon.local` | Contractor | Excavation permit applicant; registered contractor for building projects |
| `consultant@modon.local` | Consultant | Registered consulting office |
| `reviewer@modon.local` | Modon reviewer | Admin portal |
| `sadmin@modon.local` | FormCMS super admin | Admin portal (full FormCMS data access) |

## Tests

```bash
dotnet test server/Modon.Platform.Tests/Modon.Platform.Tests.csproj
cd apps/portal && npm test && npm run build
cd apps/admin  && npm test && npm run typecheck && npm run build
cd apps/mobile && npm test && npm run typecheck && npx expo-doctor
```

**Results of the last verified run** (release check for commit `fd72fd8`, 24 October 2026):

| Suite | Result |
|---|---|
| Backend (xUnit, SQLite + fake Camunda cluster) | 149 / 149 passed |
| Partner portal (Vitest) | 69 / 69 passed; type-check and production build OK |
| Admin portal (Vitest) | 39 / 39 passed; type-check and production build OK |
| Mobile app (Jest, React Native Testing Library) | 34 / 34 passed; type-check OK; production `expo export` for Android and iOS OK |
| `expo-doctor` | 19 / 20: Expo has since published newer patch releases of three packages; the lock file pins the versions the app was accepted with |

The project owner also performed manual acceptance testing on an iPhone (Expo Go), the partner portal and the admin
portal, and reported every checklist item as passing. Scripted live checks against real PostgreSQL and Camunda 8.9 were
also run during development (MODON.md §10.4).

## Known limitations

- **Notifications:** no real e-mail, SMS or mobile push delivery (see [Notifications](#notifications)).
- **Mobile app:**
  - tested on an iPhone with Expo Go only;
  - not run on Android, and no store build (`.apk`/`.aab`/`.ipa`) was produced;
  - phone testing uses plain HTTP on a private network, so production needs HTTPS.
- **Mobile sign-in:** sessions last 30 days without refresh tokens, and there is no "sign out everywhere" option.
  Reviewers have no mobile access.
- **Prototype assumptions:**
  - some dropdown values are placeholders;
  - offices are registered only by seeding;
  - where the user manuals are silent, the behaviour is an assumption listed in MODON.md.
- **Camunda:** optional per run, and only `NONE`/`BASIC` authentication is wired. Requests submitted with the built-in
  engine are not migrated into Camunda.
- **Data outside Git:**
  - uploaded documents are stored on the backend's disk (`server/Modon.Platform/App_Data/private-documents`) and are
    git-ignored;
  - request data lives in the local PostgreSQL Docker volume;
  - `.env.local`, build outputs and `node_modules` are also excluded;
  - a fresh clone therefore starts with an empty database. Moving existing data to another PC means copying the
    documents folder and a matching database dump together (MODON.md, Troubleshooting).

## Repository layout

| Path | Contents |
|---|---|
| `server/Modon.Platform/`, `server/Modon.Platform.Tests/` | Modon backend and its tests |
| `apps/portal/`, `apps/admin/`, `apps/mobile/` | Partner portal, admin portal, mobile app |
| `modon-platform.sln` | Solution with only the FormCMS and Modon projects |
| `MODON.md` | Full developer and evaluator guide |
| `server/FormCMS*`, `doc/`, `examples/`, `etc/`, `YoutubeDownloader/`, `formcms.sln` | Upstream FormCMS code and material, kept unchanged |

## Attribution and licence

Modon Service Platform is built on **[FormCMS](https://github.com/formcms/formcms)**, an open-source CMS and app platform
for .NET. FormCMS provides:
- user accounts and sign-in;
- roles;
- entities and saved queries (used for the service catalogue and lookup values);
- the notification inbox.

The FormCMS code under `server/FormCMS*` is used unchanged; all Modon code lives in `server/Modon.Platform*` and
`apps/`.

FormCMS is distributed under the MIT License, Copyright (c) 2025 FormCMS. See [LICENSE](LICENSE).
