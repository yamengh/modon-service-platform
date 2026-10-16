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
  | "cancelled";
export type ReviewStatus = Exclude<RequestStatus, "draft">;
export type RequestAction =
  | "create"
  | "submit"
  | "approve"
  | "reject"
  | "returnForCompletion"
  | "resubmit"
  | "requestCoordinationLetters"
  | "submitCoordinationLetters";
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
