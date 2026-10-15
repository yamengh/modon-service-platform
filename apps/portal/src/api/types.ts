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
