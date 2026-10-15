import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { Establishment, LookupCategory, LookupOption } from "../api/types";

export type ExcavationOptions = {
  establishments: Establishment[];
  lookups: Record<LookupCategory, LookupOption[]>;
};

/** Which lookup category feeds each dropdown of the excavation form. */
export const FIELD_CATEGORY = {
  requestTypeCode: "excavationRequestType",
  contractTypeCode: "excavationContractType",
  industrialCityCode: "industrialCity",
  permitTypeCode: "excavationPermitType",
  excavationMethodCode: "excavationMethod",
} as const satisfies Record<string, LookupCategory>;

const CATEGORIES = Object.values(FIELD_CATEGORY);

/** Loads the user's establishments and every dropdown list in parallel. */
export function useExcavationOptions(): ExcavationOptions | "loading" | "error" {
  const [state, setState] = useState<ExcavationOptions | "loading" | "error">("loading");

  useEffect(() => {
    let active = true;
    Promise.all([api.myEstablishments(), ...CATEGORIES.map((c) => api.listLookups(c))])
      .then(([establishments, ...lists]) => {
        if (!active) return;
        const lookups = Object.fromEntries(CATEGORIES.map((c, i) => [c, lists[i]])) as Record<LookupCategory, LookupOption[]>;
        setState({ establishments: establishments as Establishment[], lookups });
      })
      .catch(() => active && setState("error"));
    return () => {
      active = false;
    };
  }, []);

  return state;
}
