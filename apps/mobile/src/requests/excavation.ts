import type { ExcavationData, LookupCategory } from "../api/types";

/** Which dropdown list each excavation field uses (the portal's FIELD_CATEGORY). */
export const FIELD_CATEGORY: Partial<Record<keyof ExcavationData, LookupCategory>> = {
  requestTypeCode: "excavationRequestType",
  contractTypeCode: "excavationContractType",
  industrialCityCode: "industrialCity",
  permitTypeCode: "excavationPermitType",
  excavationMethodCode: "excavationMethod",
};

const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Phones have no date picker in plain React Native: dates are typed as yyyy-MM-dd and checked here. */
export function isValidDate(value: string): boolean {
  const m = DATE.exec(value.trim());
  if (!m) return false;
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])));
  return d.getUTCFullYear() === Number(m[1]) && d.getUTCMonth() === Number(m[2]) - 1 && d.getUTCDate() === Number(m[3]);
}
