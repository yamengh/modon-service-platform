import type {
  AttachmentSlot,
  BuildingLicenceData,
  BuildingView,
  OfficeInfo,
  PartnerProjectDetail,
  PersonaResponse,
  PhaseProgress,
  QuotationView,
  RequestDetail,
  TenderView,
} from "../api/types";

export const buildingSlots: AttachmentSlot[] = [
  { code: "approvedGeneralPlans", labelAr: "المخططات العامة المعتمدة الصادرة", labelEn: "Issued approved general plans", requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".pdf", ".png"] },
  { code: "approvedSafetyPlans", labelAr: "مخططات السلامة المعتمدة الصادرة", labelEn: "Issued approved safety plans", requiredOnSubmit: true, maxSizeKb: 20480, extensions: [".pdf", ".png"] },
  { code: "generalAttachment", labelAr: "مرفق عام خاص", labelEn: "Private general attachment", requiredOnSubmit: false, maxSizeKb: 10240, extensions: [".jpeg", ".jpg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"] },
];

export const emptyBuilding: BuildingLicenceData = {
  establishmentId: null, contractNumber: null, licenceTypeCode: null, constructionBudgetSar: null, floorCount: null,
  scopeOfWork: null, notes: null, contacts: [], termsAccepted: false,
};

export const completeBuilding: BuildingLicenceData = {
  establishmentId: 2, contractNumber: "25000168", licenceTypeCode: "new-building", constructionBudgetSar: 10_000_000, floorCount: 11,
  scopeOfWork: "نطاق العمل", notes: null, contacts: [{ name: "Sameh", email: "sameh@example.com", mobile: "01201001203" }], termsAccepted: true,
};

export const consultantOffice: OfficeInfo = { id: 30, crNumber: "7055501234", nameAr: "المكتب الهندسي الاستشاري", nameEn: "Engineering Consulting Office" };
export const otherOffice: OfficeInfo = { id: 31, crNumber: "7055509999", nameAr: "مكتب التصميم", nameEn: "Design Office" };

const zeroProgress: PhaseProgress[] = [
  "sitePreparation", "foundations", "structuralWorks", "wallsInsulationFloors", "electricalMechanicalArchitectural", "siteFinishingAndFence",
].map((phase) => ({ phase: phase as PhaseProgress["phase"], completionPercent: 0 }));

export function building(overrides: Partial<BuildingView> = {}): BuildingView {
  return {
    form: completeBuilding, tenders: [], selectedConsultant: null, consultantQuotation: null, selectedContractor: null,
    contractorQuotation: null, progressReports: [], currentProgress: zeroProgress, investorActions: [], ...overrides,
  };
}

export function quotation(overrides: Partial<QuotationView> = {}): QuotationView {
  return {
    id: 501, tenderId: 400, stage: "consultant", office: consultantOffice, state: "submitted", amountSar: 200_000, comment: "عرضنا",
    commitmentsAccepted: false, submittedAt: "2026-10-18T10:00:00Z", attachment: null, phases: [], ...overrides,
  };
}

export function tender(overrides: Partial<TenderView> = {}): TenderView {
  return {
    id: 400, stage: "consultant", mode: "openTender", number: "Bid-00400-18-10-26", invitedOffice: null, state: "open",
    openedAt: "2026-10-18T09:00:00Z", closedAt: null, quotations: [], ...overrides,
  };
}

export function buildingRequest(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return {
    id: 12,
    requestNumber: "BLR-000012-18-10-26",
    serviceCode: "building-licence",
    serviceNameAr: "رخصة البناء وتقارير البناء",
    serviceNameEn: "Building Licence & Construction Reports",
    status: "draft",
    canEdit: true,
    createdAt: "2026-10-18T08:00:00Z",
    updatedAt: "2026-10-18T08:00:00Z",
    submittedAt: null,
    excavation: {
      establishmentId: null, requestTypeCode: null, contractTypeCode: null, industrialCityCode: null, permitTypeCode: null,
      workDescription: null, issueDate: null, endDate: null, excavationMethodCode: null, workLocation: null, lengthM: null,
      widthM: null, depthM: null, totalPathLengthM: null, projectManagerName: null, projectManagerPhone: null,
      projectManagerEmail: null, supervisingEntity: null,
    },
    attachmentSlots: buildingSlots,
    attachments: [],
    history: [{ fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-18T08:00:00Z", comment: null }],
    coordinationLetterSlots: [],
    canUploadCoordinationLetters: false,
    currentTask: null,
    building: building({ form: emptyBuilding }),
    ...overrides,
  };
}

/** A submitted request waiting in consultant selection. */
export function inConsultantSelection(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return buildingRequest({
    status: "consultantSelection",
    canEdit: false,
    submittedAt: "2026-10-18T09:00:00Z",
    attachments: [
      { slotCode: "approvedGeneralPlans", fileName: "plans.pdf", contentType: "application/pdf", sizeBytes: 2048, uploadedAt: "2026-10-18T08:30:00Z" },
    ],
    history: [
      { fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-18T08:00:00Z", comment: null },
      { fromStatus: "draft", toStatus: "consultantSelection", action: "submit", occurredAt: "2026-10-18T09:00:00Z", comment: null },
    ],
    currentTask: { kind: "chooseConsultant", instructions: null, assignedAt: "2026-10-18T09:00:00Z" },
    building: building({ investorActions: ["openConsultantTender", "selectConsultant"] }),
    ...overrides,
  });
}

export const consultantPersona: PersonaResponse = {
  id: "u4",
  email: "consultant@modon.local",
  name: "consultant@modon.local",
  roles: ["consultant"],
  personas: ["consultant"],
  canUsePortal: true,
};

export function project(overrides: Partial<PartnerProjectDetail> = {}): PartnerProjectDetail {
  return {
    requestId: 12, requestNumber: "BLR-000012-18-10-26", status: "consultantTender", relation: "openTender", stage: "consultant",
    currentRound: tender(), investor: { id: 2, crNumber: "7098765432", nameAr: "مصنع الشرق للصناعات", nameEn: "Al-Sharq Industries Factory" },
    licenceTypeCode: "new-building", constructionBudgetSar: 10_000_000, floorCount: 11, scopeOfWork: "نطاق العمل", notes: null,
    submittedAt: "2026-10-18T09:00:00Z", myOffices: [consultantOffice], myQuotations: [], attachmentSlots: [], attachments: [],
    consultantPlan: null, progressReports: [], currentProgress: zeroProgress, canQuote: true, canRespond: false, canReport: false,
    ...overrides,
  };
}
