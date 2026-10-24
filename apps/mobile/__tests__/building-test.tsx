import { fireEvent } from "@testing-library/react-native";
import { renderRouter, screen } from "expo-router/testing-library";
import type { BuildingView, PartnerProjectDetail, RequestDetail } from "../src/api/types";
import {
  clearStore, consultant, contractor, excavationRequest, fakeBackend, investor, letterSlots, signedInRoutes, storeToken,
} from "../test/backend";

const APP = "./src/app";
const PHASES = ["sitePreparation", "foundations", "structuralWorks", "wallsInsulationFloors", "electricalMechanicalArchitectural", "siteFinishingAndFence"] as const;
const office = { id: 30, crNumber: "7055501234", nameAr: "المكتب الهندسي الاستشاري", nameEn: "Engineering Consulting Office" };

beforeEach(async () => {
  await clearStore();
  await storeToken();
});

const form = {
  establishmentId: 2, contractNumber: "25000168", licenceTypeCode: "new-building", constructionBudgetSar: 10000000, floorCount: 11,
  scopeOfWork: null, notes: null, contacts: [], termsAccepted: true,
};

function building(overrides: Partial<BuildingView> = {}): BuildingView {
  return {
    form, tenders: [], selectedConsultant: null, consultantQuotation: null, selectedContractor: null, contractorQuotation: null,
    progressReports: [], currentProgress: PHASES.map((phase) => ({ phase, completionPercent: 0 })), investorActions: [], ...overrides,
  };
}

function buildingRequest(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return excavationRequest({
    id: 12, requestNumber: "BLR-000012-18-10-26", serviceCode: "building-licence", serviceNameAr: "رخصة البناء وتقارير البناء",
    serviceNameEn: "Building Licence & Construction Reports", status: "consultantSelection", canEdit: false, submittedAt: "2026-10-18T09:00:00Z",
    attachmentSlots: [], coordinationLetterSlots: [],
    building: building({ investorActions: ["openConsultantTender", "selectConsultant"] }),
    ...overrides,
  });
}

function project(overrides: Partial<PartnerProjectDetail> = {}): PartnerProjectDetail {
  return {
    requestId: 12, requestNumber: "BLR-000012-18-10-26", status: "consultantTender", relation: "openTender", stage: "consultant",
    currentRound: { id: 400, stage: "consultant", mode: "openTender", number: "Bid-00400-18-10-26", invitedOffice: null, state: "open", openedAt: "2026-10-18T09:00:00Z", closedAt: null, quotations: [] },
    investor: null, licenceTypeCode: "new-building", constructionBudgetSar: 10000000, floorCount: 11, scopeOfWork: null, notes: null,
    submittedAt: "2026-10-18T09:00:00Z", myOffices: [office], myQuotations: [], attachmentSlots: [], attachments: [], consultantPlan: null,
    progressReports: [], currentProgress: PHASES.map((phase) => ({ phase, completionPercent: 0 })), canQuote: true, canRespond: false, canReport: false,
    ...overrides,
  };
}

describe("Investor: consultant and contractor stages", () => {
  it("chooses a specific consulting office from the registered offices", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(investor),
      "GET /api/modon/requests/12": { status: 200, body: buildingRequest() },
      "GET /api/modon/building/offices?stage=consultant": { status: 200, body: [office] },
      "POST /api/modon/requests/12/building/consultant/selection": {
        status: 200,
        body: buildingRequest({ status: "awaitingConsultantDecision", building: building({ tenders: [{ ...project().currentRound!, mode: "direct", number: null, invitedOffice: office }] }) }),
      },
    });
    await renderRouter(APP, { initialUrl: "/requests/12" });

    await fireEvent.press(await screen.findByRole("radio", { name: "اختيار مكتب إستشاري محدد" }));
    await fireEvent.press(await screen.findByRole("radio", { name: "المكتب الهندسي الاستشاري – 7055501234" }));
    await fireEvent.press(screen.getByRole("button", { name: "التقدم في الطلب" }));

    expect(await screen.findByText("بانتظار رد المكتب الهندسي الاستشاري.")).toBeOnTheScreen();
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({ mode: "direct", establishmentId: 30 });
  });

  it("picks one quotation of the open project", async () => {
    const round = project().currentRound!;
    const quote = { id: 501, tenderId: 400, stage: "consultant" as const, office, state: "submitted" as const, amountSar: 200000, comment: "عرضنا",
      commitmentsAccepted: false, submittedAt: "2026-10-18T10:00:00Z", attachment: null, phases: [] };
    const { calls } = fakeBackend({
      ...signedInRoutes(investor),
      "GET /api/modon/requests/12": {
        status: 200,
        body: buildingRequest({ status: "consultantTender", building: building({ tenders: [{ ...round, quotations: [quote] }], investorActions: ["acceptConsultantQuotation", "rejectConsultantQuotations"] }) }),
      },
      "POST /api/modon/requests/12/building/consultant/quotations/501/accept": {
        status: 200,
        body: buildingRequest({ status: "contractorSelection", building: building({ investorActions: ["openContractorTender", "selectContractor"] }) }),
      },
    });
    await renderRouter(APP, { initialUrl: "/requests/12" });

    await fireEvent.press(await screen.findByRole("radio", { name: /المكتب الهندسي الاستشاري — 200,000 ريال/ }));
    await fireEvent.press(screen.getByRole("button", { name: "إختيار عرض سعر" }));

    expect(await screen.findByRole("header", { name: "اختيار المقاول" })).toBeOnTheScreen();
    expect(calls.some((c) => c.path.endsWith("/quotations/501/accept"))).toBe(true);
  });

  it("checks the application's required fields and terms before submitting", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(investor),
      "GET /api/modon/requests/12": {
        status: 200,
        body: buildingRequest({ status: "draft", canEdit: true, submittedAt: null,
          attachmentSlots: [{ code: "approvedGeneralPlans", labelAr: "المخططات العامة المعتمدة الصادرة", labelEn: "Plans", requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".pdf", ".png"] }],
          building: building({ form: { ...form, contractNumber: null, termsAccepted: false } }) }),
      },
      "GET /api/modon/me/establishments": { status: 200, body: [] },
    });
    await renderRouter(APP, { initialUrl: "/requests/12/building" });

    await fireEvent.press(await screen.findByRole("button", { name: "تقديم الطلب" }));

    expect(screen.getAllByText("هذا الحقل مطلوب").length).toBeGreaterThanOrEqual(3); // contract number, plans, terms
    expect(calls.some((c) => c.method === "PUT" || c.method === "POST")).toBe(false);
  });
});

describe("Coordination letters", () => {
  it("requires all three letters before sending", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(contractor),
      "GET /api/modon/requests/7": {
        status: 200,
        body: excavationRequest({ status: "awaitingCoordinationLetters", canEdit: false, canUploadCoordinationLetters: true, coordinationLetterSlots: letterSlots,
          attachments: [{ slotCode: "secCoordinationLetter", fileName: "sec.pdf", contentType: "application/pdf", sizeBytes: 10, uploadedAt: "2026-10-16T10:00:00Z" }] }),
      },
    });
    await renderRouter(APP, { initialUrl: "/requests/7/letters" });

    await fireEvent.press(await screen.findByRole("button", { name: "إرسال خطابات التنسيق" }));

    expect(screen.getAllByText("هذا الحقل مطلوب")).toHaveLength(2);
    expect(calls.some((c) => c.path.endsWith("/coordination-letters/submit"))).toBe(false);
  });
});

describe("Consulting offices", () => {
  it("submits a quotation for an open project", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(consultant),
      "GET /api/modon/building/projects/12": { status: 200, body: project() },
      "POST /api/modon/building/projects/12/quotations": { status: 200, body: project({ canQuote: false }) },
    });
    await renderRouter(APP, { initialUrl: "/projects/12" });

    await fireEvent.changeText(await screen.findByLabelText("قيمة عرض السعر"), "150000");
    await fireEvent.press(screen.getByRole("button", { name: "تقديم" }));

    expect(await screen.findByText(/تم تقديم عرض السعر/)).toBeOnTheScreen();
    expect(calls.find((c) => c.method === "POST")!.body).toEqual({ establishmentId: 30, amountSar: 150000, comment: null });
  });

  it("requires the phase table and commitments to accept an invitation", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(consultant),
      "GET /api/modon/building/projects/12": { status: 200, body: project({ relation: "invited", canQuote: false, canRespond: true }) },
    });
    await renderRouter(APP, { initialUrl: "/projects/12" });

    await fireEvent.press(await screen.findByRole("radio", { name: "قبول" }));
    await fireEvent.changeText(screen.getByLabelText("قيمة عرض السعر"), "4000000");
    await fireEvent.press(screen.getByRole("button", { name: "تقديم" }));

    expect(screen.getAllByText("هذا الحقل مطلوب").length).toBeGreaterThanOrEqual(13); // 6 shares, 6 durations, commitments
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  });

  it("checks each monthly percentage is between 0 and 100", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(consultant),
      "GET /api/modon/building/projects/12": { status: 200, body: project({ relation: "awarded", status: "approved", canQuote: false, canReport: true }) },
    });
    await renderRouter(APP, { initialUrl: "/projects/12" });

    await fireEvent.changeText(await screen.findByLabelText("تجهيز الموقع – نسبة الإنجاز (%)"), "120");
    await fireEvent.press(screen.getByRole("button", { name: "تقديم التقرير" }));

    expect(screen.getByText("يجب أن تكون النسبة بين 0 و 100")).toBeOnTheScreen();
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  });

  it("opens an office invitation from the task list", async () => {
    fakeBackend({
      ...signedInRoutes(consultant),
      "GET /api/modon/tasks": {
        status: 200,
        body: [{ requestId: 12, requestNumber: "BLR-000012-18-10-26", serviceCode: "building-licence", serviceNameAr: "رخصة البناء", serviceNameEn: "Building",
          status: "awaitingConsultantDecision", kind: "respondAsConsultant", instructions: null, assignedAt: "2026-10-18T09:00:00Z", submittedAt: null }],
      },
      "GET /api/modon/building/projects/12": { status: 200, body: project({ relation: "invited", canQuote: false, canRespond: true }) },
    });
    await renderRouter(APP, { initialUrl: "/tasks" });

    await fireEvent.press(await screen.findByRole("button", { name: "الرد على اختيار المستثمر" }));

    expect(screen).toHavePathname("/projects/12");
  });
});

describe("Notifications and language", () => {
  it("lists my notifications and marks one read when opened", async () => {
    const n = { id: 41, kind: "requestUpdate", requestId: 7, requestNumber: "EXC-000007-15-10-26", serviceCode: "excavation-permit", action: "returnForCompletion",
      status: "returnedForCompletion", titleAr: "أعادت مدن الطلب لإكمال النواقص — EXC-000007-15-10-26", titleEn: "Modon returned the request — EXC-000007-15-10-26",
      comment: "صحح موقع العمل", link: "/requests/7", createdAt: "2026-10-16T09:00:00Z", read: false };
    const { calls } = fakeBackend({
      ...signedInRoutes(contractor),
      "GET /api/modon/notifications?page=1": { status: 200, body: { items: [n], total: 1, unreadCount: 1, page: 1, pageSize: 20 } },
      "POST /api/modon/notifications/41/read": { status: 204 },
      "GET /api/modon/requests/7": { status: 200, body: excavationRequest() },
    });
    await renderRouter(APP, { initialUrl: "/notifications" });

    expect(await screen.findByText("أعادت مدن الطلب لإكمال النواقص — EXC-000007-15-10-26")).toBeOnTheScreen();
    expect(screen.getByText("صحح موقع العمل")).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole("button", { name: "فتح" }));

    expect(calls.some((c) => c.method === "POST" && c.path === "/api/modon/notifications/41/read")).toBe(true);
    expect(screen).toHavePathname("/requests/7");
  });

  it("switches to English from the More tab", async () => {
    fakeBackend(signedInRoutes(contractor));
    await renderRouter(APP, { initialUrl: "/more" });

    await fireEvent.press(await screen.findByRole("radio", { name: "English" }));

    expect(await screen.findByRole("button", { name: "Sign out" })).toBeOnTheScreen();
    expect(screen.getByText("Account")).toBeOnTheScreen();
  });
});
