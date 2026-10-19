import { useState } from "react";
import { api } from "../api/client";
import type { RequestWorkflowReport, ReviewRequestDetail } from "../api/types";
import { useI18n } from "../i18n/I18nContext";

type Check = { status: "idle" } | { status: "checking" } | { status: "error" } | { status: "done"; report: RequestWorkflowReport };

/** The request's workflow engine and Camunda process instance, with an on-demand consistency check. */
export function WorkflowPanel({ request }: { request: ReviewRequestDetail }) {
  const { t } = useI18n();
  const [check, setCheck] = useState<Check>({ status: "idle" });
  const w = request.workflow;

  async function compare() {
    setCheck({ status: "checking" });
    try {
      setCheck({ status: "done", report: await api.getRequestWorkflow(request.id) });
    } catch {
      setCheck({ status: "error" });
    }
  }

  return (
    <div className="card workflow-panel">
      <h2>{t("workflowEngine")}</h2>
      <dl className="read-grid">
        <div>
          <dt>{t("workflowEngine")}</dt>
          <dd>{t(`engine.${w.engine}`)}</dd>
        </div>
        <div>
          <dt>{t("processInstance")}</dt>
          <dd dir="ltr">{w.processInstanceKey ?? "—"}</dd>
        </div>
        <div>
          <dt>{t("processVersion")}</dt>
          <dd dir="ltr">{w.processDefinitionVersion ?? "—"}</dd>
        </div>
      </dl>
      {w.processInstanceKey && (
        <button type="button" className="btn secondary small" onClick={() => void compare()} disabled={check.status === "checking"}>
          {check.status === "checking" ? t("loading") : t("workflowCheck")}
        </button>
      )}
      {check.status === "error" && (
        <p className="alert" role="alert">
          {t("workflowStatusFailed")}
        </p>
      )}
      {check.status === "done" && (
        <p className={check.report.inSync === true ? "notice" : "alert"} role="status">
          {check.report.inSync === true
            ? t("inSync")
            : check.report.inSync === false
              ? `${t("outOfSync")} ${check.report.engineStatus ? t(`status.${check.report.engineStatus}`) : check.report.engineElementId}`
              : `${t("syncUnknown")} ${check.report.note ?? ""}`}
        </p>
      )}
    </div>
  );
}
