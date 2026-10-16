import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api } from "../api/client";
import type { LookupCategory, LookupOption, ServiceSummary } from "../api/types";

const CATEGORIES: LookupCategory[] = [
  "industrialCity",
  "excavationRequestType",
  "excavationContractType",
  "excavationPermitType",
  "excavationMethod",
];

interface ReferenceData {
  services: ServiceSummary[];
  /** Option for a code, or undefined while loading / if unknown (callers then show the raw code). */
  option: (category: LookupCategory, code: string | null | undefined) => LookupOption | undefined;
}

const Context = createContext<ReferenceData>({ services: [], option: () => undefined });

/**
 * Loads the catalogue and dropdown lists once (public FormCMS saved queries). If loading fails the
 * portal still works and simply shows codes instead of names.
 */
export function ReferenceDataProvider({ children }: { children: ReactNode }) {
  const [services, setServices] = useState<ServiceSummary[]>([]);
  const [lookups, setLookups] = useState<Partial<Record<LookupCategory, LookupOption[]>>>({});

  useEffect(() => {
    let active = true;
    api.listServices().then((s) => active && setServices(s)).catch(() => undefined);
    for (const category of CATEGORIES) {
      api
        .listLookups(category)
        .then((options) => active && setLookups((l) => ({ ...l, [category]: options })))
        .catch(() => undefined);
    }
    return () => {
      active = false;
    };
  }, []);

  const value = useMemo<ReferenceData>(
    () => ({
      services,
      option: (category, code) => (code ? lookups[category]?.find((o) => o.code === code) : undefined),
    }),
    [services, lookups],
  );

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export const useReferenceData = () => useContext(Context);
