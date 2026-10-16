import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { RequestSummary } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { useI18n } from "../i18n/I18nContext";
import { formatDate } from "../i18n/format";
import { actionPath } from "../requests/serviceAvailability";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; requests: RequestSummary[] };

export function MyRequestsPage() {
  const { t, pick } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    api
      .listMyRequests()
      .then((requests) => active && setLoad({ status: "ready", requests }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, []);

  return (
    <main className="page">
      <h1>{t("myRequests")}</h1>
      <p className="muted">{t("myRequestsIntro")}</p>

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <p className="alert" role="alert">
          {t("loadFailed")}
        </p>
      )}
      {load.status === "ready" && load.requests.length === 0 && (
        <p className="status">
          {t("noRequests")} <Link to="/services">{t("modonServices")}</Link>
        </p>
      )}
      {load.status === "ready" && load.requests.length > 0 && (
        <div className="table-wrap card">
          <table className="requests">
            <thead>
              <tr>
                <th>{t("requestNumber")}</th>
                <th>{t("service")}</th>
                <th>{t("status")}</th>
                <th>{t("createdAt")}</th>
                <th>{t("submittedAt")}</th>
                <th>{t("actions")}</th>
              </tr>
            </thead>
            <tbody>
              {load.requests.map((r) => {
                const next = actionPath(r.serviceCode, r.id, r.status);
                return (
                  <tr key={r.id}>
                    <td>
                      <bdi>{r.requestNumber}</bdi>
                    </td>
                    <td>{pick(r, "serviceName")}</td>
                    <td>
                      <StatusBadge status={r.status} />
                    </td>
                    <td>{formatDate(r.createdAt)}</td>
                    <td>{formatDate(r.submittedAt)}</td>
                    <td>
                      {next ? (
                        <Link to={next}>
                          {r.status === "draft"
                            ? t("continueEditing")
                            : t(r.status === "returnedForCompletion" ? "task.completeReturnedRequest" : "task.uploadCoordinationLetters")}
                        </Link>
                      ) : (
                        <Link to={`/requests/${r.id}`}>{t("view")}</Link>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
}
