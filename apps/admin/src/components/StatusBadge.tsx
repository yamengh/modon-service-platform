import type { RequestStatus } from "../api/types";
import { useI18n } from "../i18n/I18nContext";

export function StatusBadge({ status }: { status: RequestStatus }) {
  const { t } = useI18n();
  return <span className={`status-badge status-${status}`}>{t(`status.${status}`)}</span>;
}
