import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api } from "../api/client";
import type { BuildingView, OfficeInfo, QuotationView, ReviewRequestDetail } from "../api/types";
import { renderApp, reviewDetail, summary, useReviewerBackend, workflowReady } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const consultant: OfficeInfo = { id: 30, crNumber: "7055501234", nameAr: "المكتب الهندسي الاستشاري", nameEn: "Engineering Consulting Office" };
const contractor: OfficeInfo = { id: 40, crNumber: "7012345678", nameAr: "مؤسسة البناء المتقدم للمقاولات", nameEn: "Advanced Building Contracting Est." };

const consultantQuote: QuotationView = {
  id: 501, tenderId: 400, stage: "consultant", office: consultant, state: "selected", amountSar: 4_000_000, comment: "نقبل",
  commitmentsAccepted: true, submittedAt: "2026-10-18T10:00:00Z",
  attachment: { fileName: "plan.xlsx", contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sizeBytes: 2048 },
  phases: [{ phase: "sitePreparation", sharePercent: 4, durationDays: 30 }, { phase: "foundations", sharePercent: 4, durationDays: 10 }],
};
const contractorQuote: QuotationView = {
  ...consultantQuote, id: 601, tenderId: 410, stage: "contractor", office: contractor, amountSar: 2_000_000, comment: null,
  commitmentsAccepted: false, attachment: null, phases: [],
};

function buildingView(overrides: Partial<BuildingView> = {}): BuildingView {
  return {
    form: {
      establishmentId: 2, contractNumber: "25000168", licenceTypeCode: "new-building", constructionBudgetSar: 10_000_000, floorCount: 11,
      scopeOfWork: "نطاق العمل", notes: "لا يوجد", contacts: [{ name: "Sameh", email: "sameh@example.com", mobile: "01201001203" }], termsAccepted: true,
    },
    tenders: [
      { id: 400, stage: "consultant", mode: "direct", number: null, invitedOffice: consultant, state: "awarded", openedAt: "2026-10-18T09:00:00Z",
        closedAt: "2026-10-18T10:00:00Z", quotations: [consultantQuote] },
      { id: 410, stage: "contractor", mode: "openTender", number: "Bid-00410-18-10-26", invitedOffice: null, state: "awarded",
        openedAt: "2026-10-18T11:00:00Z", closedAt: "2026-10-18T12:00:00Z",
        quotations: [contractorQuote, { ...contractorQuote, id: 602, state: "notSelected", amountSar: 2_500_000, office: { ...contractor, id: 41, nameAr: "مقاول آخر" } }] },
    ],
    selectedConsultant: consultant,
    consultantQuotation: consultantQuote,
    selectedContractor: contractor,
    contractorQuotation: contractorQuote,
    progressReports: [],
    currentProgress: [],
    investorActions: [],
    ...overrides,
  };
}

function buildingDetail(overrides: Partial<ReviewRequestDetail> = {}): ReviewRequestDetail {
  return reviewDetail({
    id: 21,
    requestNumber: "BLR-000021-18-10-26",
    serviceCode: "building-licence",
    serviceNameAr: "رخصة البناء وتقارير البناء",
    serviceNameEn: "Building Licence & Construction Reports",
    status: "underModonReview",
    establishment: { id: 2, crNumber: "7098765432", nameAr: "مصنع الشرق للصناعات", nameEn: "Al-Sharq Industries Factory" },
    attachmentSlots: [
      { code: "approvedGeneralPlans", labelAr: "المخططات العامة المعتمدة الصادرة", labelEn: "Issued approved general plans", requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".pdf", ".png"] },
    ],
    attachments: [{ slotCode: "approvedGeneralPlans", fileName: "plans.pdf", contentType: "application/pdf", sizeBytes: 4096, uploadedAt: "2026-10-18T08:00:00Z" }],
    availableDecisions: ["approve", "reject"],
    coordinationLetterSlots: [],
    building: buildingView(),
    ...overrides,
  });
}

beforeEach(() => {
  useReviewerBackend();
  vi.mocked(api.buildingFileUrl).mockImplementation((id, kind, fileId) => `/api/modon/review/requests/${id}/building/${kind}/${fileId}/attachment`);
});

describe("Building licence review", () => {
  it("shows the application, contacts, chosen offices with quotations and phases, and every round", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(buildingDetail());
    renderApp("/requests/21");

    expect(await screen.findByRole("heading", { name: "رخصة البناء وتقارير البناء" })).toBeInTheDocument();
    expect(screen.getByText("25000168")).toBeInTheDocument();
    expect(screen.getByText("10,000,000 ريال")).toBeInTheDocument();
    expect(screen.getByText("ترخيص بناء جديد")).toBeInTheDocument();
    expect(screen.getByText("sameh@example.com")).toBeInTheDocument();
    expect(screen.queryByText("نوع الطلب")).not.toBeInTheDocument(); // no excavation fields

    const offices = within(screen.getByRole("heading", { name: "المكتب الاستشاري والمقاول" }).parentElement!);
    expect(offices.getByText("4,000,000 ريال")).toBeInTheDocument();
    expect(offices.getByText("تم التحقق من تعهدات المكتب الاستشاري")).toBeInTheDocument();
    expect(offices.getByRole("link", { name: "plan.xlsx" })).toHaveAttribute("href", "/api/modon/review/requests/21/building/quotations/501/attachment");
    expect(within(offices.getByRole("table", { name: "مراحل المشروع" })).getByText("تجهيز الموقع")).toBeInTheDocument();

    const rounds = within(screen.getByRole("heading", { name: "جولات الاختيار وعروض الأسعار" }).parentElement!);
    expect(rounds.getByText("Bid-00410-18-10-26")).toBeInTheDocument();
    expect(rounds.getByText("2,500,000 ريال")).toBeInTheDocument();
    expect(rounds.getByText("غير مختار")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "plans.pdf" })).toBeInTheDocument();
  });

  it("offers only approve and reject, and records the reviewer's comment", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(buildingDetail());
    vi.mocked(api.approve).mockResolvedValue(buildingDetail({ status: "approved", availableDecisions: [] }));
    renderApp("/requests/21");

    const buttons = within(await screen.findByRole("group", { name: "قرار المراجعة" }));
    expect(buttons.getAllByRole("button").map((b) => b.textContent)).toEqual(["اعتماد الطلب", "رفض الطلب"]);
    expect(screen.getByText(/راجع رخص البناء المرفقة/)).toBeInTheDocument();
    await userEvent.click(buttons.getByRole("button", { name: "اعتماد الطلب" }));
    await userEvent.type(screen.getByLabelText("ملاحظة (اختياري)"), "الرخص مكتملة");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(api.approve).toHaveBeenCalledWith(21, "الرخص مكتملة");
    expect(await screen.findByText("تم اعتماد الطلب.")).toBeInTheDocument();
  });

  it("shows construction progress and the monthly reports of an approved licence, in English", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(
      buildingDetail({
        status: "approved",
        availableDecisions: [],
        building: buildingView({
          currentProgress: [{ phase: "sitePreparation", completionPercent: 100 }, { phase: "structuralWorks", completionPercent: 35.5 }],
          progressReports: [{
            id: 9, period: "2026-10", office: consultant, submittedByUserId: "u4", submittedByEmail: "consultant@modon.local",
            submittedAt: "2026-10-18T12:00:00Z", comment: "On schedule", attachment: { fileName: "report.pdf", contentType: "application/pdf", sizeBytes: 1024 },
            phases: [{ phase: "sitePreparation", completionPercent: 100 }, { phase: "structuralWorks", completionPercent: 35.5 }],
          }],
        }),
      }),
    );
    renderApp("/requests/21", "en");

    expect(await screen.findByRole("heading", { name: "Monthly construction-progress reports" })).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Structural works" })).toHaveAttribute("aria-valuenow", "35.5");
    expect(screen.getByText("consultant@modon.local", { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "report.pdf" })).toHaveAttribute("href", "/api/modon/review/requests/21/building/progress-reports/9/attachment");
    expect(screen.getByText("On schedule")).toBeInTheDocument();
  });

  it("lists the building statuses in the queue filter and on the dashboard", async () => {
    vi.mocked(api.getSummary).mockResolvedValue({ ...summary, total: 12, byStatus: { ...summary.byStatus, underModonReview: 3 } });
    renderApp("/");

    expect(await screen.findByRole("link", { name: "دراسة الطلب: 3" })).toHaveAttribute("href", "/requests?status=underModonReview");
    expect(screen.getByRole("link", { name: "معلقة على قرار المقاول: 0" })).toBeInTheDocument();
  });

  it("shows each Camunda process on the workflow card, and which one is not deployed", async () => {
    vi.mocked(api.getSummary).mockResolvedValue(summary);
    vi.mocked(api.getWorkflowStatus).mockResolvedValue({
      ...workflowReady,
      ready: false,
      summary: "Camunda 8.9.21 at http://127.0.0.1:8080: Process 'modon-building-licence' is not deployed. Deploy building-licence.bpmn (see MODON.md).",
      camunda: {
        ...workflowReady.camunda!,
        processes: [workflowReady.camunda!.processes[0], { ...workflowReady.camunda!.processes[1], deployed: false, deployedVersion: null }],
      },
    });
    renderApp("/");

    const card = within(await screen.findByRole("region", { name: "محرك سير العمل" }));
    expect(card.getByText("modon-excavation-permit v2")).toBeInTheDocument();
    expect(card.getByText("(modon-building-licence)")).toBeInTheDocument();
    expect(card.getByText("غير منشورة")).toBeInTheDocument();
  });
});
