import { ApiError } from "../api/client";
import { useI18n, type MessageKey } from "../i18n/I18nContext";

/** A user-facing message for a failed call: offline, not configured, workflow unavailable, changed meanwhile… */
export function useFailureText() {
  const { t } = useI18n();
  return (e: unknown, fallback: MessageKey = "genericError"): string => {
    if (!(e instanceof ApiError)) return t(fallback);
    if (e.message === "apiNotConfigured") return t("apiNotConfigured");
    if (e.offline) return t("networkError");
    if (e.status === 503) return t("workflowUnavailable");
    if (e.status === 409 && e.message === "workflowNotStarted") return t("workflowNotStarted");
    if (e.status === 409) return t("stageChanged");
    if (e.status === 404) return t("requestNotFound");
    return t(fallback);
  };
}
