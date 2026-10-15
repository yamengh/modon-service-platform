import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { App } from "../App";
import type {
  AttachmentInfo,
  AttachmentSlot,
  Establishment,
  ExcavationData,
  LookupCategory,
  LookupOption,
  PersonaResponse,
  RequestDetail,
  ServiceDetail,
} from "../api/types";
import { AuthProvider } from "../auth/AuthContext";
import { I18nProvider } from "../i18n/I18nContext";
import type { Lang } from "../i18n/messages";

/** Renders the whole app at a route. Tests mock ../api/client to control backend responses. */
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

export const investor: PersonaResponse = {
  id: "u1",
  email: "investor@modon.local",
  name: "investor@modon.local",
  roles: ["investor"],
  personas: ["investor"],
  canUsePortal: true,
};

export const reviewer: PersonaResponse = {
  id: "u2",
  email: "reviewer@modon.local",
  name: "reviewer@modon.local",
  roles: ["modon_reviewer"],
  personas: [],
  canUsePortal: false,
};

export const buildingLicence: ServiceDetail = {
  id: 1,
  code: "building-licence",
  nameAr: "رخصة البناء وتقارير البناء",
  nameEn: "Building Licence & Construction Reports",
  summaryAr: "ملخص رخصة البناء",
  summaryEn: "Building licence summary",
  beneficiary: "investor",
  sortOrder: 1,
};

export const excavationPermit: ServiceDetail = {
  id: 2,
  code: "excavation-permit",
  nameAr: "تصريح أعمال الحفر",
  nameEn: "Excavation Works Permit",
  summaryAr: "ملخص تصريح الحفر",
  summaryEn: "Excavation permit summary",
  beneficiary: "contractor",
  durationAr: "3 أيام عمل",
  durationEn: "3 working days",
  sortOrder: 2,
  stepsAr: "الدخول على بوابة مدن\nاختيار خدمات مدن\nاختيار تصريح أعمال الحفر",
  stepsEn: "Sign in\nOpen Modon services\nChoose Excavation Works Permit",
  requiredDocumentsAr: "مخطط مسار العمل\nخطة سلامة السير",
  requiredDocumentsEn: "Work route plan\nTraffic safety plan",
};

// --- Milestone 2 fixtures -------------------------------------------------------------------

export const contractor: PersonaResponse = {
  id: "u3",
  email: "contractor@modon.local",
  name: "contractor@modon.local",
  roles: ["contractor"],
  personas: ["contractor"],
  canUsePortal: true,
};

const imagesAndPdf = [".jpeg", ".pdf", ".jpg", ".png"];
export const excavationSlots: AttachmentSlot[] = [
  { code: "workLocationPhoto", labelAr: "صورة لموقع العمل", labelEn: "Work location photo", requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".jpeg", ".jpg", ".png"] },
  { code: "workRoutePlan", labelAr: "مخطط مسار العمل", labelEn: "Work route plan", requiredOnSubmit: true, maxSizeKb: 20480, extensions: imagesAndPdf },
  { code: "excavationCrossSection", labelAr: "مقطع عرضي للحفر", labelEn: "Excavation cross-section", requiredOnSubmit: true, maxSizeKb: 20480, extensions: imagesAndPdf },
  { code: "projectSchedule", labelAr: "الجدول الزمني للمشروع", labelEn: "Project schedule", requiredOnSubmit: true, maxSizeKb: 20480, extensions: imagesAndPdf },
  { code: "trafficSafetyPlan", labelAr: "خطة سلامة السير", labelEn: "Traffic safety plan", requiredOnSubmit: true, maxSizeKb: 20480, extensions: imagesAndPdf },
  { code: "ownerEntityLetter", labelAr: "خطاب الجهة المالكة", labelEn: "Owner entity letter", requiredOnSubmit: true, maxSizeKb: 20480, extensions: imagesAndPdf },
];

export const emptyExcavation: ExcavationData = {
  establishmentId: null, requestTypeCode: null, contractTypeCode: null, industrialCityCode: null,
  permitTypeCode: null, workDescription: null, issueDate: null, endDate: null, excavationMethodCode: null,
  workLocation: null, lengthM: null, widthM: null, depthM: null, totalPathLengthM: null,
  projectManagerName: null, projectManagerPhone: null, projectManagerEmail: null, supervisingEntity: null,
};

export const completeExcavation: ExcavationData = {
  establishmentId: 1, requestTypeCode: "new", contractTypeCode: "bill-of-quantities", industrialCityCode: "riyadh-2",
  permitTypeCode: "electricity", workDescription: "حفر لتمديد كابلات", issueDate: "2026-11-01", endDate: "2026-11-30",
  excavationMethodCode: "open-trench", workLocation: "شارع 12", lengthM: 120.5, widthM: null, depthM: null,
  totalPathLengthM: null, projectManagerName: "أحمد علي", projectManagerPhone: "+966500000000",
  projectManagerEmail: "pm@example.com", supervisingEntity: "مكتب الإشراف",
};

export const allAttachments: AttachmentInfo[] = excavationSlots.map((s) => ({
  slotCode: s.code, fileName: `${s.code}.pdf`, contentType: "application/pdf", sizeBytes: 2048, uploadedAt: "2026-10-15T10:00:00Z",
}));

export function excavationRequest(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return {
    id: 7,
    requestNumber: "EXC-000007-15-10-26",
    serviceCode: "excavation-permit",
    serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit",
    status: "draft",
    canEdit: true,
    createdAt: "2026-10-15T10:00:00Z",
    updatedAt: "2026-10-15T10:00:00Z",
    submittedAt: null,
    excavation: emptyExcavation,
    attachmentSlots: excavationSlots,
    attachments: [],
    history: [{ fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T10:00:00Z", comment: null }],
    ...overrides,
  };
}

export const establishments: Establishment[] = [
  { id: 1, crNumber: "7012345678", nameAr: "مؤسسة البناء المتقدم للمقاولات", nameEn: "Advanced Building Contracting Est." },
];

export const lookups: Record<LookupCategory, LookupOption[]> = {
  industrialCity: [
    { code: "riyadh-2", nameAr: "المدينة الصناعية الثانية بالرياض", nameEn: "Riyadh 2nd Industrial City" },
    { code: "dammam-2", nameAr: "المدينة الصناعية الثانية بالدمام", nameEn: "Dammam 2nd Industrial City" },
  ],
  excavationRequestType: [{ code: "new", nameAr: "طلب جديد", nameEn: "New request" }],
  excavationContractType: [{ code: "bill-of-quantities", nameAr: "مقايسة", nameEn: "Bill of quantities" }],
  excavationPermitType: [{ code: "electricity", nameAr: "تمديدات كهربائية", nameEn: "Electrical works" }],
  excavationMethod: [{ code: "open-trench", nameAr: "حفر مكشوف", nameEn: "Open trench" }],
};
