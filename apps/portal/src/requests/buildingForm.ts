import type { AttachmentInfo, AttachmentSlot, BuildingLicenceData, FieldErrors } from "../api/types";

/** The licence types the manual documents (the backend accepts the same list). */
export const LICENCE_TYPES = ["new-building"] as const;

export interface ContactForm {
  name: string;
  email: string;
  mobile: string;
}

/** Form state: inputs are strings exactly as typed; the terms box is a boolean. */
export interface BuildingForm {
  establishmentId: string;
  contractNumber: string;
  licenceTypeCode: string;
  constructionBudgetSar: string;
  floorCount: string;
  scopeOfWork: string;
  notes: string;
  contacts: ContactForm[];
  termsAccepted: boolean;
}

/** Marked with * in the manual's form. */
export const REQUIRED_BUILDING_FIELDS = [
  "establishmentId",
  "contractNumber",
  "licenceTypeCode",
  "constructionBudgetSar",
  "floorCount",
] as const;

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PHONE = /^\+?[0-9][0-9 ]{6,18}$/;
const CONTRACT = /^[0-9A-Za-z/-]+$/;
const MAX_AMOUNT = 9_999_999_999;
export const MAX_CONTACTS = 10;

const str = (v: unknown) => (v === null || v === undefined ? "" : String(v));

export function toBuildingForm(data: BuildingLicenceData): BuildingForm {
  return {
    establishmentId: str(data.establishmentId),
    contractNumber: str(data.contractNumber),
    licenceTypeCode: str(data.licenceTypeCode),
    constructionBudgetSar: str(data.constructionBudgetSar),
    floorCount: str(data.floorCount),
    scopeOfWork: str(data.scopeOfWork),
    notes: str(data.notes),
    contacts: (data.contacts ?? []).map((c) => ({ name: str(c.name), email: str(c.email), mobile: str(c.mobile) })),
    termsAccepted: data.termsAccepted,
  };
}

export function toBuildingData(form: BuildingForm): BuildingLicenceData {
  const text = (v: string) => (v.trim() === "" ? null : v.trim());
  const num = (v: string) => (v.trim() === "" ? null : Number(v));
  return {
    establishmentId: num(form.establishmentId),
    contractNumber: text(form.contractNumber),
    licenceTypeCode: text(form.licenceTypeCode),
    constructionBudgetSar: num(form.constructionBudgetSar),
    floorCount: num(form.floorCount),
    scopeOfWork: text(form.scopeOfWork),
    notes: text(form.notes),
    contacts: form.contacts
      .map((c) => ({ name: text(c.name), email: text(c.email), mobile: text(c.mobile) }))
      .filter((c) => c.name || c.email || c.mobile),
    termsAccepted: form.termsAccepted,
  };
}

/**
 * Same rules as the backend's BuildingValidator, for instant feedback. "draft" checks formats only;
 * "submit" also requires the starred fields, the terms and the plans. Field names match the backend's.
 */
export function validateBuilding(
  form: BuildingForm,
  mode: "draft" | "submit",
  slots: AttachmentSlot[] = [],
  attachments: AttachmentInfo[] = [],
): FieldErrors {
  const errors: FieldErrors = {};
  const add = (field: string, code: string) => {
    if (!errors[field]) errors[field] = [code];
  };
  const submit = mode === "submit";

  if (submit) for (const field of REQUIRED_BUILDING_FIELDS) if (!form[field].trim()) add(field, "required");

  const contract = form.contractNumber.trim();
  if (contract.length > 30) add("contractNumber", "tooLong");
  else if (contract && !CONTRACT.test(contract)) add("contractNumber", "invalidFormat");

  const budget = form.constructionBudgetSar.trim();
  if (budget) {
    const n = Number(budget);
    if (!Number.isFinite(n)) add("constructionBudgetSar", "invalidNumber");
    else if (n <= 0) add("constructionBudgetSar", "mustBePositive");
    else if (n > MAX_AMOUNT) add("constructionBudgetSar", "tooLarge");
  }
  const floors = form.floorCount.trim();
  if (floors) {
    const n = Number(floors);
    if (!Number.isInteger(n)) add("floorCount", "wholeNumber");
    else if (n < 1) add("floorCount", "mustBePositive");
    else if (n > 200) add("floorCount", "tooLarge");
  }
  if (form.scopeOfWork.trim().length > 2000) add("scopeOfWork", "tooLong");
  if (form.notes.trim().length > 2000) add("notes", "tooLong");

  const contacts = form.contacts.filter((c) => c.name.trim() || c.email.trim() || c.mobile.trim());
  if (contacts.length > MAX_CONTACTS) add("contacts", "tooMany");
  contacts.forEach((c, i) => {
    if (!c.name.trim()) add(`contacts[${i}].name`, "required");
    if (!c.email.trim()) add(`contacts[${i}].email`, "required");
    else if (!EMAIL.test(c.email.trim())) add(`contacts[${i}].email`, "invalidEmail");
    if (!c.mobile.trim()) add(`contacts[${i}].mobile`, "required");
    else if (!PHONE.test(c.mobile.trim())) add(`contacts[${i}].mobile`, "invalidPhone");
  });

  if (submit) {
    if (!form.termsAccepted) add("termsAccepted", "required");
    const uploaded = new Set(attachments.map((a) => a.slotCode));
    for (const slot of slots) if (slot.requiredOnSubmit && !uploaded.has(slot.code)) add(`attachments.${slot.code}`, "required");
  }
  return errors;
}
