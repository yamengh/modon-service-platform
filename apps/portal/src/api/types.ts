/** Response of GET /api/modon/me/personas (Modon.Platform). */
export interface PersonaResponse {
  id: string;
  email: string;
  name: string;
  roles: string[];
  personas: Persona[];
  canUsePortal: boolean;
}

export type Persona = "investor" | "contractor" | "consultant";

export type Beneficiary = "investor" | "contractor";

/** A row of the FormCMS saved query "modonServices". */
export interface ServiceSummary {
  id: number;
  code: string;
  nameAr: string;
  nameEn: string;
  summaryAr: string;
  summaryEn: string;
  beneficiary: Beneficiary;
  durationAr?: string;
  durationEn?: string;
  sortOrder: number;
}

/** A dropdown option from the FormCMS saved query "modonLookups". */
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

export interface Establishment {
  id: number;
  crNumber: string;
  nameAr: string;
  nameEn: string;
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

/** Excavation permit form data. Dates are "yyyy-MM-dd"; every field may be null in a draft. */
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

export interface HistoryItem {
  fromStatus: RequestStatus | null;
  toStatus: RequestStatus;
  action: RequestAction;
  occurredAt: string;
  comment: string | null;
}

export interface RequestSummary {
  id: number;
  requestNumber: string;
  serviceCode: string;
  serviceNameAr: string;
  serviceNameEn: string;
  status: RequestStatus;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
}

export interface RequestDetail extends RequestSummary {
  /** The application form and its attachments can be edited (draft, or returned for completion). */
  canEdit: boolean;
  excavation: ExcavationData;
  attachmentSlots: AttachmentSlot[];
  /** Application attachments and, once uploaded, coordination letters. */
  attachments: AttachmentInfo[];
  history: HistoryItem[];
  /** Coordination letters Modon may request for this service (empty when the service has none). */
  coordinationLetterSlots: AttachmentSlot[];
  /** Modon is waiting for the coordination letters: they can be uploaded and sent now. */
  canUploadCoordinationLetters: boolean;
  /** What Modon is waiting for the applicant to do, with the reviewer's instructions. */
  currentTask: ApplicantTask | null;
  /** Building licence only: form, rounds and quotations, selected offices, progress reports. */
  building: BuildingView | null;
}

export type ApplicantTaskKind =
  | "completeReturnedRequest"
  | "uploadCoordinationLetters"
  | "chooseConsultant"
  | "reviewConsultantQuotations"
  | "chooseContractor"
  | "reviewContractorQuotations"
  | "respondAsConsultant"
  | "respondAsContractor";

export interface ApplicantTask {
  kind: ApplicantTaskKind;
  instructions: string | null;
  assignedAt: string;
}

/** A row of GET /api/modon/tasks: a request waiting for the signed-in user. */
export interface TaskItem {
  requestId: number;
  requestNumber: string;
  serviceCode: string;
  serviceNameAr: string;
  serviceNameEn: string;
  status: RequestStatus;
  kind: ApplicantTaskKind;
  instructions: string | null;
  assignedAt: string;
  submittedAt: string | null;
}

/** Field → error codes, as returned in a 400 ValidationProblem. */
export type FieldErrors = Record<string, string[]>;

/** The FormCMS saved query "modonService" (single). Multi-line fields use "\n". */
export interface ServiceDetail extends ServiceSummary {
  channelAr?: string;
  channelEn?: string;
  costAr?: string;
  costEn?: string;
  descriptionAr?: string;
  descriptionEn?: string;
  stepsAr?: string;
  stepsEn?: string;
  requiredDocumentsAr?: string;
  requiredDocumentsEn?: string;
}

// --- Building licence (Milestones 6–8) --------------------------------------------------------------

export interface BuildingContact {
  name: string | null;
  email: string | null;
  mobile: string | null;
}

/** Building licence form data (طلب رخصة بناء). Every field may be empty in a draft. */
export interface BuildingLicenceData {
  establishmentId: number | null;
  contractNumber: string | null;
  licenceTypeCode: string | null;
  constructionBudgetSar: number | null;
  floorCount: number | null;
  scopeOfWork: string | null;
  notes: string | null;
  contacts: BuildingContact[];
  termsAccepted: boolean;
}

export type BuildingStage = "consultant" | "contractor";
export type SelectionMode = "openTender" | "direct";
export type TenderState = "open" | "awarded" | "closed";
export type QuotationState = "submitted" | "selected" | "notSelected" | "declined";
export type PartnerRelation = "openTender" | "invited" | "awarded";
export type OfficeDecision = "accept" | "reject";
export type ConstructionPhase =
  | "sitePreparation"
  | "foundations"
  | "structuralWorks"
  | "wallsInsulationFloors"
  | "electricalMechanicalArchitectural"
  | "siteFinishingAndFence";

/** A consulting office or contractor (commercial registration). */
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

export interface PhasePlan {
  phase: ConstructionPhase;
  sharePercent: number | null;
  durationDays: number | null;
}

export interface PhaseProgress {
  phase: ConstructionPhase;
  completionPercent: number | null;
}

export interface QuotationView {
  id: number;
  tenderId: number;
  stage: BuildingStage;
  office: OfficeInfo;
  state: QuotationState;
  amountSar: number | null;
  comment: string | null;
  commitmentsAccepted: boolean;
  submittedAt: string;
  attachment: StoredFile | null;
  phases: PhasePlan[];
}

export interface TenderView {
  id: number;
  stage: BuildingStage;
  mode: SelectionMode;
  number: string | null;
  invitedOffice: OfficeInfo | null;
  state: TenderState;
  openedAt: string;
  closedAt: string | null;
  quotations: QuotationView[];
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
  /** Stage actions the investor may take now (the backend decides). */
  investorActions: RequestAction[];
}

/** A row of GET /api/modon/building/projects. */
export interface PartnerProjectSummary {
  requestId: number;
  requestNumber: string;
  projectNumber: string | null;
  status: RequestStatus;
  stage: BuildingStage;
  relation: PartnerRelation;
  licenceTypeCode: string | null;
  investor: OfficeInfo | null;
  since: string;
  answered: boolean;
}

/** GET /api/modon/building/projects/{id}: what an office may see of a project and do with it. */
export interface PartnerProjectDetail {
  requestId: number;
  requestNumber: string;
  status: RequestStatus;
  relation: PartnerRelation;
  stage: BuildingStage;
  currentRound: TenderView | null;
  investor: OfficeInfo | null;
  licenceTypeCode: string | null;
  constructionBudgetSar: number | null;
  floorCount: number | null;
  scopeOfWork: string | null;
  notes: string | null;
  submittedAt: string | null;
  myOffices: OfficeInfo[];
  myQuotations: QuotationView[];
  attachmentSlots: AttachmentSlot[];
  attachments: AttachmentInfo[];
  consultantPlan: QuotationView | null;
  progressReports: ProgressReportView[];
  currentProgress: PhaseProgress[];
  canQuote: boolean;
  canRespond: boolean;
  canReport: boolean;
}

export interface OfficeResponse {
  decision: OfficeDecision;
  amountSar: number | null;
  comment: string | null;
  commitmentsAccepted: boolean;
  phases: PhasePlan[] | null;
}

export interface ProgressReportInput {
  period: string;
  phases: PhaseProgress[];
  comment: string | null;
}

// --- Notifications (final phase) ---------------------------------------------------------------------

export type NotificationKind =
  | "requestUpdate"
  | "officeInvited"
  | "quotationSelected"
  | "quotationNotSelected"
  | "projectApproved"
  | "projectRejected";

/** One of the signed-in user's in-app notifications (GET /api/modon/notifications). */
export interface NotificationView {
  id: number;
  kind: NotificationKind;
  requestId: number;
  requestNumber: string;
  serviceCode: string;
  action: RequestAction;
  status: RequestStatus;
  titleAr: string;
  titleEn: string;
  comment: string | null;
  /** Portal path: /requests/{id} for applicants, /projects/{id} for offices. */
  link: string;
  createdAt: string;
  read: boolean;
}

export interface NotificationPage {
  items: NotificationView[];
  total: number;
  unreadCount: number;
  page: number;
  pageSize: number;
}
