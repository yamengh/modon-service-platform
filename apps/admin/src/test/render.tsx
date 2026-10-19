import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { App } from "../App";
import { api } from "../api/client";
import type {
  LookupCategory,
  LookupOption,
  Me,
  ReviewQueueItem,
  ReviewRequestDetail,
  ReviewSummary,
  WorkflowStatusReport,
} from "../api/types";
import { AuthProvider } from "../auth/AuthContext";
import { I18nProvider } from "../i18n/I18nContext";
import type { Lang } from "../i18n/messages";

/**
 * Renders the whole admin app at a route. Test files mock "../api/client" with every api function
 * replaced by vi.fn():
 *
 *   vi.mock("../api/client", async (importOriginal) => {
 *     const actual = await importOriginal<typeof import("../api/client")>();
 *     return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
 *   });
 */
export function renderApp(route: string, lang: Lang = "ar") {
  return render(
    <I18nProvider lang={lang}>
      <AuthProvider>
        <MemoryRouter initialEntries={[route]}>
          <App />
        </MemoryRouter>
      </AuthProvider>
    </I18nProvider>,
  );
}

export const reviewer: Me = {
  id: "r1", email: "reviewer@modon.local", name: "reviewer@modon.local", roles: ["modon_reviewer"],
  personas: [], canUsePortal: false, canReview: true,
};

export const contractor: Me = {
  id: "c1", email: "contractor@modon.local", name: "contractor@modon.local", roles: ["contractor"],
  personas: ["contractor"], canUsePortal: true, canReview: false,
};

export const lookups: Record<LookupCategory, LookupOption[]> = {
  industrialCity: [{ code: "riyadh-2", nameAr: "المدينة الصناعية الثانية بالرياض", nameEn: "Riyadh 2nd Industrial City" }],
  excavationRequestType: [{ code: "new", nameAr: "طلب جديد", nameEn: "New request" }],
  excavationContractType: [{ code: "bill-of-quantities", nameAr: "مقايسة", nameEn: "Bill of quantities" }],
  excavationPermitType: [{ code: "electricity", nameAr: "تمديدات كهربائية", nameEn: "Electrical works" }],
  excavationMethod: [{ code: "open-trench", nameAr: "حفر مكشوف", nameEn: "Open trench" }],
};

/** Default backend for a signed-in reviewer with reference data available. */
export function useReviewerBackend(user: Me = reviewer) {
  vi.mocked(api.getMe).mockResolvedValue(user);
  vi.mocked(api.listServices).mockResolvedValue([
    { code: "building-licence", nameAr: "رخصة البناء وتقارير البناء", nameEn: "Building Licence & Construction Reports" },
    { code: "excavation-permit", nameAr: "تصريح أعمال الحفر", nameEn: "Excavation Works Permit" },
  ]);
  vi.mocked(api.listLookups).mockImplementation(async (category: LookupCategory) => lookups[category]);
  vi.mocked(api.attachmentUrl).mockImplementation((id: number, slot: string) => `/api/modon/review/requests/${id}/attachments/${slot}`);
  vi.mocked(api.getWorkflowStatus).mockResolvedValue(workflowReady);
}

export const workflowReady: WorkflowStatusReport = {
  engine: "camunda",
  ready: true,
  summary: "Camunda 8.9.21 at http://127.0.0.1:8080: process 'modon-excavation-permit' version 2 is deployed.",
  camunda: {
    restAddress: "http://127.0.0.1:8080", authStrategy: "NONE", reachable: true, gatewayVersion: "8.9.21", brokers: 1,
    partitionsHealthy: true, processId: "modon-excavation-permit", processDeployed: true, deployedVersion: 2,
    deployedVersionTag: "1.0.0", checkedInVersionTag: "1.0.0", processDefinitionKey: "2251799813721445", error: null,
  },
};

export const summary: ReviewSummary = {
  total: 9,
  byStatus: { inProgress: 4, returnedForCompletion: 2, awaitingCoordinationLetters: 0, approved: 2, rejected: 1, cancelled: 0 },
};

export function queueItem(overrides: Partial<ReviewQueueItem> = {}): ReviewQueueItem {
  return {
    id: 11,
    requestNumber: "EXC-000011-15-10-26",
    serviceCode: "excavation-permit",
    serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit",
    status: "inProgress",
    submittedAt: "2026-10-15T10:00:00Z",
    updatedAt: "2026-10-15T10:00:00Z",
    industrialCityCode: "riyadh-2",
    applicant: { userId: "c1", email: "contractor@modon.local", name: "contractor@modon.local", phone: null },
    establishment: { id: 1, crNumber: "7012345678", nameAr: "مؤسسة البناء المتقدم للمقاولات", nameEn: "Advanced Building Contracting Est." },
    ...overrides,
  };
}

const slot = (code: string, labelAr: string, labelEn: string) => ({
  code, labelAr, labelEn, requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".jpeg", ".pdf", ".jpg", ".png"],
});

export function reviewDetail(overrides: Partial<ReviewRequestDetail> = {}): ReviewRequestDetail {
  return {
    id: 11,
    requestNumber: "EXC-000011-15-10-26",
    serviceCode: "excavation-permit",
    serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit",
    status: "inProgress",
    createdAt: "2026-10-15T09:00:00Z",
    updatedAt: "2026-10-15T10:00:00Z",
    submittedAt: "2026-10-15T10:00:00Z",
    applicant: { userId: "c1", email: "contractor@modon.local", name: "contractor@modon.local", phone: "+966500000001" },
    establishment: { id: 1, crNumber: "7012345678", nameAr: "مؤسسة البناء المتقدم للمقاولات", nameEn: "Advanced Building Contracting Est." },
    excavation: {
      establishmentId: 1, requestTypeCode: "new", contractTypeCode: "bill-of-quantities", industrialCityCode: "riyadh-2",
      permitTypeCode: "electricity", workDescription: "حفر لتمديد كابلات", issueDate: "2026-11-01", endDate: "2026-11-30",
      excavationMethodCode: "open-trench", workLocation: "شارع 12", lengthM: 120.5, widthM: null, depthM: null,
      totalPathLengthM: null, projectManagerName: "أحمد علي", projectManagerPhone: "+966500000000",
      projectManagerEmail: "pm@example.com", supervisingEntity: "مكتب الإشراف",
    },
    attachmentSlots: [slot("workLocationPhoto", "صورة لموقع العمل", "Work location photo"), slot("workRoutePlan", "مخطط مسار العمل", "Work route plan")],
    attachments: [{ slotCode: "workRoutePlan", fileName: "route.pdf", contentType: "application/pdf", sizeBytes: 4096, uploadedAt: "2026-10-15T09:30:00Z" }],
    history: [
      { fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T09:00:00Z", comment: null, actorUserId: "c1", actorEmail: "contractor@modon.local", actorIsApplicant: true },
      { fromStatus: "draft", toStatus: "inProgress", action: "submit", occurredAt: "2026-10-15T10:00:00Z", comment: null, actorUserId: "c1", actorEmail: "contractor@modon.local", actorIsApplicant: true },
    ],
    availableDecisions: ["approve", "returnForCompletion", "reject"],
    workflow: { engine: "camunda", processInstanceKey: "2251799813721500", processDefinitionVersion: 2 },
    coordinationLetterSlots: [
      slot("secCoordinationLetter", "خطاب تنسيق الشركة السعودية للكهرباء", "Saudi Electricity Company coordination letter"),
      slot("nationalGridCoordinationLetter", "خطاب تنسيق الشركة الوطنية لنقل الكهرباء", "National Grid coordination letter"),
      slot("eastElectricityOfficeCoordinationLetter", "خطاب تنسيق مكتب خدمة كهرباء شرق", "East electricity service office coordination letter"),
    ],
    ...overrides,
  };
}
