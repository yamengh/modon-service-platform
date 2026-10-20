/** GET /api/modon/me/personas — who is signed in and what they may do. */
export interface Me {
  id: string;
  email: string;
  name: string;
  roles: string[];
  personas: string[];
  canUsePortal: boolean;
  canReview: boolean;
}

export type RequestStatus =
  | "draft"
  | "inProgress"
  | "returnedForCompletion"
  | "awaitingCoordinationLetters"
  | "approved"
  | "rejected"
  | "cancelled"
  // building licence
  | "consultantSelection"
  | "consultantTender"
  | "awaitingConsultantDecision"
  | "contractorSelection"
  | "contractorTender"
  | "awaitingContractorDecision"
  | "underModonReview";
export type ReviewStatus = Exclude<RequestStatus, "draft">;
export type RequestAction =
  | "create"
  | "submit"
  | "approve"
  | "reject"
  | "returnForCompletion"
  | "resubmit"
  | "requestCoordinationLetters"
  | "submitCoordinationLetters"
  // building licence
  | "openConsultantTender"
  | "selectConsultant"
  | "acceptConsultantQuotation"
  | "rejectConsultantQuotations"
  | "consultantAccepted"
  | "consultantDeclined"
  | "openContractorTender"
  | "selectContractor"
  | "acceptContractorQuotation"
  | "rejectContractorQuotations"
  | "contractorAccepted"
  | "contractorDeclined"
  | "submitProgressReport";
export type Decision = "approve" | "requestCoordinationLetters" | "returnForCompletion" | "reject";

export interface ApplicantInfo {
  userId: string;
  email: string;
  name: string;
  phone: string | null;
}

export interface EstablishmentInfo {
  id: number;
  crNumber: string;
  nameAr: string;
  nameEn: string;
}

export interface ReviewQueueItem {
  id: number;
  requestNumber: string;
  serviceCode: string;
  serviceNameAr: string;
  serviceNameEn: string;
  status: ReviewStatus;
  submittedAt: string | null;
  updatedAt: string;
  industrialCityCode: string | null;
  applicant: ApplicantInfo;
  establishment: EstablishmentInfo | null;
}

export interface ReviewQueuePage {
  items: ReviewQueueItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ReviewSummary {
  total: number;
  byStatus: Record<ReviewStatus, number>;
}

export interface ExcavationData {
  establishmentId: number | null;
  requestTypeCode: string | null;
  contractTypeCode: string | null;
  industrialCityCode: string | null;
  permitTypeCode: string | null;
  workDescription: string | null;
  issueDate: string | null;
  endDate: string | null;
  excavationMethodCode: string | null;
  workLocation: string | null;
  lengthM: number | null;
  widthM: number | null;
  depthM: number | null;
  totalPathLengthM: number | null;
  projectManagerName: string | null;
  projectManagerPhone: string | null;
  projectManagerEmail: string | null;
  supervisingEntity: string | null;
}

export interface AttachmentSlot {
  code: string;
  labelAr: string;
  labelEn: string;
  requiredOnSubmit: boolean;
  maxSizeKb: number;
  extensions: string[];
}

export interface AttachmentInfo {
  slotCode: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
}

export interface ReviewHistoryItem {
  fromStatus: RequestStatus | null;
  toStatus: RequestStatus;
  action: RequestAction;
  occurredAt: string;
  comment: string | null;
  actorUserId: string;
  actorEmail: string | null;
  actorIsApplicant: boolean;
}

export interface ReviewRequestDetail {
  id: number;
  requestNumber: string;
  serviceCode: string;
  serviceNameAr: string;
  serviceNameEn: string;
  status: RequestStatus;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
  applicant: ApplicantInfo;
  establishment: EstablishmentInfo | null;
  excavation: ExcavationData;
  attachmentSlots: AttachmentSlot[];
  attachments: AttachmentInfo[];
  history: ReviewHistoryItem[];
  availableDecisions: Decision[];
  /** Coordination letters the applicant uploads when asked (shown once uploaded). */
  coordinationLetterSlots: AttachmentSlot[];
  workflow: ReviewWorkflowInfo;
  /** Building licence only: form, contacts, rounds and quotations, selected offices, progress reports. */
  building: BuildingView | null;
}

export type WorkflowEngine = "inProcess" | "camunda";

/** Which engine handles the request and, for Camunda, its process instance. */
export interface ReviewWorkflowInfo {
  engine: WorkflowEngine;
  processInstanceKey: string | null;
  processDefinitionVersion: number | null;
}

/** GET /api/modon/review/workflow — checked live by the backend. */
export interface WorkflowStatusReport {
  engine: WorkflowEngine;
  ready: boolean;
  summary: string;
  camunda: {
    restAddress: string;
    authStrategy: string;
    reachable: boolean;
    gatewayVersion: string | null;
    brokers: number | null;
    partitionsHealthy: boolean;
    processId: string;
    processDeployed: boolean;
    deployedVersion: number | null;
    deployedVersionTag: string | null;
    checkedInVersionTag: string | null;
    processDefinitionKey: string | null;
    error: string | null;
    /** Every process Modon drives (excavation permit, building licence), each with its deployment. */
    processes: CamundaProcessReport[];
  } | null;
}

export interface CamundaProcessReport {
  serviceCode: string;
  processId: string;
  bpmnFile: string;
  deployed: boolean;
  deployedVersion: number | null;
  deployedVersionTag: string | null;
  checkedInVersionTag: string | null;
  processDefinitionKey: string | null;
}

/** GET /api/modon/review/requests/{id}/workflow — the database status next to Camunda's. */
export interface RequestWorkflowReport {
  engine: WorkflowEngine;
  requestNumber: string;
  databaseStatus: RequestStatus;
  processInstanceKey: string | null;
  processDefinitionVersion: number | null;
  engineState: string | null;
  engineElementId: string | null;
  engineStatus: RequestStatus | null;
  inSync: boolean | null;
  note: string | null;
}

export interface QueueFilters {
  status?: string;
  service?: string;
  search?: string;
  page?: number;
}

/** A catalogue service (FormCMS saved query "modonServices"). */
export interface ServiceSummary {
  code: string;
  nameAr: string;
  nameEn: string;
}

export interface LookupOption {
  code: string;
  nameAr: string;
  nameEn: string;
}

export type LookupCategory =
  | "industrialCity"
  | "excavationRequestType"
  | "excavationContractType"
  | "excavationPermitType"
  | "excavationMethod";

/** Field → error codes, as returned in a 400 ValidationProblem. */
export type FieldErrors = Record<string, string[]>;

// --- Building licence (Milestones 6–8) --------------------------------------------------------------

export type BuildingStage = "consultant" | "contractor";
export type ConstructionPhase =
  | "sitePreparation"
  | "foundations"
  | "structuralWorks"
  | "wallsInsulationFloors"
  | "electricalMechanicalArchitectural"
  | "siteFinishingAndFence";

export interface BuildingLicenceData {
  establishmentId: number | null;
  contractNumber: string | null;
  licenceTypeCode: string | null;
  constructionBudgetSar: number | null;
  floorCount: number | null;
  scopeOfWork: string | null;
  notes: string | null;
  contacts: { name: string | null; email: string | null; mobile: string | null }[];
  termsAccepted: boolean;
}

export interface OfficeInfo {
  id: number;
  crNumber: string;
  nameAr: string;
  nameEn: string;
}

export interface StoredFile {
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface QuotationView {
  id: number;
  tenderId: number;
  stage: BuildingStage;
  office: OfficeInfo;
  state: "submitted" | "selected" | "notSelected" | "declined";
  amountSar: number | null;
  comment: string | null;
  commitmentsAccepted: boolean;
  submittedAt: string;
  attachment: StoredFile | null;
  phases: { phase: ConstructionPhase; sharePercent: number | null; durationDays: number | null }[];
}

export interface TenderView {
  id: number;
  stage: BuildingStage;
  mode: "openTender" | "direct";
  number: string | null;
  invitedOffice: OfficeInfo | null;
  state: "open" | "awarded" | "closed";
  openedAt: string;
  closedAt: string | null;
  quotations: QuotationView[];
}

export interface PhaseProgress {
  phase: ConstructionPhase;
  completionPercent: number | null;
}

export interface ProgressReportView {
  id: number;
  period: string;
  office: OfficeInfo;
  submittedByUserId: string;
  submittedByEmail: string | null;
  submittedAt: string;
  comment: string | null;
  attachment: StoredFile | null;
  phases: PhaseProgress[];
}

export interface BuildingView {
  form: BuildingLicenceData;
  tenders: TenderView[];
  selectedConsultant: OfficeInfo | null;
  consultantQuotation: QuotationView | null;
  selectedContractor: OfficeInfo | null;
  contractorQuotation: QuotationView | null;
  progressReports: ProgressReportView[];
  currentProgress: PhaseProgress[];
  investorActions: RequestAction[];
}
