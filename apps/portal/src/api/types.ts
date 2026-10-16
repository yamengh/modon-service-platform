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

export type RequestStatus = "draft" | "inProgress" | "returnedForCompletion" | "approved" | "rejected" | "cancelled";
export type RequestAction = "create" | "submit" | "approve" | "reject" | "returnForCompletion";

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
  canEdit: boolean;
  excavation: ExcavationData;
  attachmentSlots: AttachmentSlot[];
  attachments: AttachmentInfo[];
  history: HistoryItem[];
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
