import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { WorkflowStatusReport } from "../api/types";
import { useI18n } from "../i18n/I18nContext";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; report: WorkflowStatusReport };

/** Which workflow engine the backend uses and whether it really works (checked live by the backend). */
export function WorkflowStatusCard() {
  const { t } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    api
      .getWorkflowStatus()
      .then((report) => active && setLoad({ status: "ready", report }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, []);

  if (load.status === "loading") return null;
  if (load.status === "error")
    return (
      <p className="alert" role="alert">
        {t("workflowStatusFailed")}
      </p>
    );

  const { report } = load;
  const camunda = report.camunda;
  return (
    <section className={`card workflow-status ${report.ready ? "ok" : "bad"}`} aria-label={t("workflowEngine")}>
      <h2>
        {t("workflowEngine")}: {t(`engine.${report.engine}`)}{" "}
        <span className={`pill ${report.ready ? "ok" : "bad"}`}>{report.ready ? t("workflowReady") : t("workflowNotReady")}</span>
      </h2>
      {camunda && (
        <dl className="read-grid">
          <div>
            <dt>{t("camundaAddress")}</dt>
            <dd dir="ltr">{camunda.restAddress}</dd>
          </div>
          <div>
            <dt>{t("camundaVersion")}</dt>
            <dd dir="ltr">{camunda.gatewayVersion ?? "—"}</dd>
          </div>
          <div>
            <dt>{t("processDeployed")}</dt>
            <dd dir="ltr">
              {camunda.processDeployed ? `${camunda.processId} v${camunda.deployedVersion}` : t("notDeployed")}
            </dd>
          </div>
        </dl>
      )}
      <p className="meta" dir="ltr">
        {report.summary}
      </p>
    </section>
  );
}
