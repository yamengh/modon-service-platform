import type { AttachmentInfo, AttachmentSlot, ExcavationData, FieldErrors } from "../api/types";

/** Form state: every input is a string, exactly as typed. */
export type ExcavationForm = Record<keyof ExcavationData, string>;

export const TEXT_FIELDS = [
  "workDescription",
  "workLocation",
  "projectManagerName",
  "projectManagerPhone",
  "projectManagerEmail",
  "supervisingEntity",
] as const;

export const SELECT_FIELDS = [
  "establishmentId",
  "requestTypeCode",
  "contractTypeCode",
  "industrialCityCode",
  "permitTypeCode",
  "excavationMethodCode",
] as const;

export const DATE_FIELDS = ["issueDate", "endDate"] as const;

/** Optional: the manual does not mark the dimensions as required. */
export const DIMENSION_FIELDS = ["lengthM", "widthM", "depthM", "totalPathLengthM"] as const;

export const REQUIRED_FIELDS: (keyof ExcavationData)[] = [...SELECT_FIELDS, ...DATE_FIELDS, ...TEXT_FIELDS];

const MAX_LENGTH: Partial<Record<keyof ExcavationData, number>> = {
  workDescription: 2000,
  workLocation: 200,
  projectManagerName: 200,
  projectManagerEmail: 200,
  supervisingEntity: 200,
  projectManagerPhone: 20,
};

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^\+?[0-9][0-9 ]{6,18}$/;
const MAX_DIMENSION = 100_000;

export function toForm(data: ExcavationData): ExcavationForm {
  const form = {} as ExcavationForm;
  for (const [key, value] of Object.entries(data) as [keyof ExcavationData, unknown][]) {
    form[key] = value === null || value === undefined ? "" : String(value);
  }
  return form;
}

export function toData(form: ExcavationForm): ExcavationData {
  const text = (v: string) => (v.trim() === "" ? null : v.trim());
  const num = (v: string) => (v.trim() === "" ? null : Number(v));
  return {
    establishmentId: num(form.establishmentId),
    requestTypeCode: text(form.requestTypeCode),
    contractTypeCode: text(form.contractTypeCode),
    industrialCityCode: text(form.industrialCityCode),
    permitTypeCode: text(form.permitTypeCode),
    workDescription: text(form.workDescription),
    issueDate: text(form.issueDate),
    endDate: text(form.endDate),
    excavationMethodCode: text(form.excavationMethodCode),
    workLocation: text(form.workLocation),
    lengthM: num(form.lengthM),
    widthM: num(form.widthM),
    depthM: num(form.depthM),
    totalPathLengthM: num(form.totalPathLengthM),
    projectManagerName: text(form.projectManagerName),
    projectManagerPhone: text(form.projectManagerPhone),
    projectManagerEmail: text(form.projectManagerEmail),
    supervisingEntity: text(form.supervisingEntity),
  };
}

/**
 * Same rules as the backend's ExcavationValidator, for instant feedback.
 * "draft" checks formats only; "submit" also requires every required field and attachment.
 */
export function validateExcavation(
  form: ExcavationForm,
  mode: "draft" | "submit",
  slots: AttachmentSlot[] = [],
  attachments: AttachmentInfo[] = [],
): FieldErrors {
  const errors: FieldErrors = {};
  const add = (field: string, code: string) => {
    if (!errors[field]) errors[field] = [code];
  };
  const value = (f: keyof ExcavationData) => form[f].trim();

  if (mode === "submit") {
    for (const field of REQUIRED_FIELDS) if (!value(field)) add(field, "required");
  }

  for (const [field, max] of Object.entries(MAX_LENGTH) as [keyof ExcavationData, number][]) {
    if (value(field).length > max) add(field, "tooLong");
  }
  if (value("projectManagerEmail") && !EMAIL.test(value("projectManagerEmail"))) add("projectManagerEmail", "invalidEmail");
  if (value("projectManagerPhone") && !PHONE.test(value("projectManagerPhone"))) add("projectManagerPhone", "invalidPhone");

  if (value("issueDate") && value("endDate") && value("endDate") <= value("issueDate")) {
    add("endDate", "endDateMustBeAfterIssueDate");
  }

  for (const field of DIMENSION_FIELDS) {
    if (!value(field)) continue;
    const n = Number(value(field));
    if (!Number.isFinite(n)) add(field, "invalidNumber");
    else if (n <= 0) add(field, "mustBePositive");
    else if (n > MAX_DIMENSION) add(field, "tooLarge");
  }

  if (mode === "submit") {
    const uploaded = new Set(attachments.map((a) => a.slotCode));
    for (const slot of slots) {
      if (slot.requiredOnSubmit && !uploaded.has(slot.code)) add(`attachments.${slot.code}`, "required");
    }
  }
  return errors;
}

/** Client-side check before uploading; returns an error code or null. */
export function checkFile(file: File, slot: AttachmentSlot): string | null {
  if (file.size === 0) return "fileEmpty";
  if (file.size > slot.maxSizeKb * 1024) return "fileTooLarge";
  const dot = file.name.lastIndexOf(".");
  const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";
  return slot.extensions.includes(ext) ? null : "fileTypeNotAllowed";
}
