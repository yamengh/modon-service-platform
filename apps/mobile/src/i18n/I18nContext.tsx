import * as SecureStore from "expo-secure-store";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { messages as portalMessages, type Lang, type MessageKey as PortalKey } from "@portal/i18n/messages";
import { mobileMessages, type MobileKey } from "./mobileMessages";

export type MessageKey = PortalKey | MobileKey;

interface I18n {
  lang: Lang;
  /** Arabic is right-to-left; components lay out rows and align text with it. */
  rtl: boolean;
  t: (key: MessageKey) => string;
  /** The Arabic or English variant of a bilingual field, e.g. pick(service, "name"). */
  pick: <T extends object>(item: T, field: string) => string;
  setLang: (lang: Lang) => void;
}

const I18nContext = createContext<I18n | null>(null);
const LANG_KEY = "modon.lang";

/**
 * Arabic (RTL) by default, English on request — the same texts as the web portal (its message file is reused) plus a
 * few app-only ones. Direction is handled by the app's components (not I18nManager), so switching language needs no
 * restart. The choice is remembered on the device (not secret; kept with the platform storage the app already uses).
 */
export function I18nProvider({ children, lang: fixed }: { children: ReactNode; lang?: Lang }) {
  const [lang, setLangState] = useState<Lang>(fixed ?? "ar");

  useEffect(() => {
    if (fixed) return;
    SecureStore.getItemAsync(LANG_KEY)
      .then((saved) => saved === "en" && setLangState("en"))
      .catch(() => undefined);
  }, [fixed]);

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    SecureStore.setItemAsync(LANG_KEY, next).catch(() => undefined);
  }, []);

  const value = useMemo<I18n>(() => {
    const suffix = lang === "ar" ? "Ar" : "En";
    const table = { ...portalMessages[lang], ...mobileMessages[lang] } as Record<MessageKey, string>;
    return {
      lang,
      rtl: lang === "ar",
      setLang,
      t: (key) => table[key] ?? key,
      pick: (item, field) => String((item as Record<string, unknown>)[field + suffix] ?? ""),
    };
  }, [lang, setLang]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used inside <I18nProvider>");
  return ctx;
}

/** Error code → translated message (backend validation codes are the portal's `err.*` keys). */
export function useErrorText() {
  const { t } = useI18n();
  return (code: string) => {
    const key = `err.${code}`;
    return key in portalMessages.ar || key in mobileMessages.ar ? t(key as MessageKey) : t("genericError");
  };
}
