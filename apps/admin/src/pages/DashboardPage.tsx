import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { ReviewStatus, ReviewSummary } from "../api/types";
import { WorkflowStatusCard } from "../components/WorkflowStatusCard";
import { useI18n } from "../i18n/I18nContext";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; summary: ReviewSummary };

/** Shown in this order; statuses with no transitions yet (cancelled) still appear for completeness. */
const STATUSES: ReviewStatus[] = ["inProgress", "returnedForCompletion", "awaitingCoordinationLetters", "approved", "rejected", "cancelled"];

export function DashboardPage() {
  const { t } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  const fetchSummary = useCallback(() => {
    setLoad({ status: "loading" });
    api
      .getSummary()
      .then((summary) => setLoad({ status: "ready", summary }))
      .catch(() => setLoad({ status: "error" }));
  }, []);

  useEffect(fetchSummary, [fetchSummary]);

  return (
    <main className="page">
      <h1>{t("dashboard")}</h1>
      <p className="muted">{t("dashboardIntro")}</p>
      <WorkflowStatusCard />

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <div className="alert" role="alert">
          {t("loadFailed")}{" "}
          <button type="button" className="link-btn" onClick={fetchSummary}>
            {t("retry")}
          </button>
        </div>
      )}
      {load.status === "ready" && (
        <>
          <div className="stat-grid">
            <Link to="/requests" className="card stat total" aria-label={`${t("totalRequests")}: ${load.summary.total}`}>
              <span className="stat-value">{load.summary.total}</span>
              <span className="stat-label">{t("totalRequests")}</span>
            </Link>
            {STATUSES.map((s) => (
              <Link
                key={s}
                to={`/requests?status=${s}`}
                className={`card stat status-edge-${s}`}
                aria-label={`${t(`status.${s}`)}: ${load.summary.byStatus[s] ?? 0}`}
              >
                <span className="stat-value">{load.summary.byStatus[s] ?? 0}</span>
                <span className="stat-label">{t(`status.${s}`)}</span>
              </Link>
            ))}
          </div>
          <p>
            <Link className="btn primary" to="/requests?status=inProgress">
              {t("openQueue")}
            </Link>
          </p>
        </>
      )}
    </main>
  );
}
