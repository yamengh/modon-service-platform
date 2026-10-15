import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { messages, type Lang, type MessageKey } from "./messages";

interface I18n {
  lang: Lang;
  t: (key: MessageKey) => string;
  toggleLang: () => void;
  /** Picks the Arabic or English variant of a bilingual field, e.g. pick(service, "name"). */
  pick: <T extends object>(item: T, field: string) => string;
}

const I18nContext = createContext<I18n | null>(null);
const STORAGE_KEY = "modon.lang";

function initialLang(): Lang {
  try {
    return localStorage.getItem(STORAGE_KEY) === "en" ? "en" : "ar";
  } catch {
    return "ar";
  }
}

export function I18nProvider({ children, lang: fixedLang }: { children: ReactNode; lang?: Lang }) {
  const [lang, setLang] = useState<Lang>(fixedLang ?? initialLang);

  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // storage unavailable (private mode); the choice just isn't remembered
    }
  }, [lang]);

  const toggleLang = useCallback(() => setLang((l) => (l === "ar" ? "en" : "ar")), []);

  const value = useMemo<I18n>(() => {
    const suffix = lang === "ar" ? "Ar" : "En";
    return {
      lang,
      toggleLang,
      t: (key) => messages[lang][key],
      pick: (item, field) => String((item as Record<string, unknown>)[field + suffix] ?? ""),
    };
  }, [lang, toggleLang]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used inside <I18nProvider>");
  return ctx;
}
