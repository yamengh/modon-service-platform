import type { ReactNode } from "react";
import type { ApplicantTask } from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { formatDateTime } from "../i18n/format";

/** What Modon is waiting for: the reviewer's instructions and when they were given. */
export function TaskPanel({ task, children }: { task: ApplicantTask; children?: ReactNode }) {
  const { t } = useI18n();
  return (
    <section className="card task-panel" aria-labelledby="task-title">
      <h2 id="task-title">{task.kind === "completeReturnedRequest" ? t("returnedTitle") : t("lettersTitle")}</h2>
      <p className="meta">
        {t("taskDate")}: {formatDateTime(task.assignedAt)}
      </p>
      <p className="task-instructions">
        <strong>{t("instructions")}:</strong> {task.instructions ?? t("noInstructions")}
      </p>
      {children}
    </section>
  );
}
