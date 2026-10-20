import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { api } from "../api/client";
import type { PartnerProjectSummary, PartnerRelation } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { useI18n } from "../i18n/I18nContext";
import { formatDate } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; projects: PartnerProjectSummary[] };

const GROUPS: [PartnerRelation, MessageKey][] = [
  ["invited", "projectsInvited"],
  ["openTender", "projectsOpen"],
  ["awarded", "projectsAwarded"],
];

/**
 * The consulting office's / contractor's workspace (manual 1.3 "المشاريع"): open projects to quote for, requests
 * where an investor chose the office directly, and awarded projects. The backend decides what each office sees.
 */
export function ProjectsPage() {
  const { t, pick } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const declined = (useLocation().state as { declined?: boolean } | null)?.declined === true;

  useEffect(() => {
    let active = true;
    api
      .listProjects()
      .then((projects) => active && setLoad({ status: "ready", projects }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, []);

  return (
    <main className="page">
      <h1>{t("projects")}</h1>
      <p className="muted">{t("projectsIntro")}</p>
      {declined && (
        <p className="notice" role="status">
          {t("declineSent")}
        </p>
      )}
      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <p className="alert" role="alert">
          {t("loadFailed")}
        </p>
      )}
      {load.status === "ready" &&
        GROUPS.map(([relation, title]) => {
          const rows = load.projects.filter((p) => p.relation === relation);
          return (
            <section key={relation} aria-labelledby={`projects-${relation}`}>
              <h2 id={`projects-${relation}`}>{t(title)}</h2>
              {rows.length === 0 ? (
                <p className="status">{t("noProjects")}</p>
              ) : (
                <div className="table-wrap card">
                  <table className="requests">
                    <thead>
                      <tr>
                        <th>{relation === "openTender" ? t("projectNumber") : t("requestNumber")}</th>
                        <th>{t("projectType")}</th>
                        <th>{t("facilityName")}</th>
                        <th>{t("status")}</th>
                        <th>{t("registrationDate")}</th>
                        <th>{t("actions")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((p) => (
                        <tr key={`${relation}-${p.requestId}`}>
                          <td>
                            <bdi>{p.projectNumber ?? p.requestNumber}</bdi>
                          </td>
                          <td>{p.licenceTypeCode ? t(`licenceType.${p.licenceTypeCode}` as MessageKey) ?? p.licenceTypeCode : "—"}</td>
                          <td>{p.investor ? pick(p.investor, "name") : "—"}</td>
                          <td>
                            <StatusBadge status={p.status} />
                            {p.answered && relation === "openTender" && <span className="badge">{t("quoted")}</span>}
                          </td>
                          <td>{formatDate(p.since)}</td>
                          <td>
                            <Link to={`/projects/${p.requestId}`}>{t("open")}</Link>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          );
        })}
    </main>
  );
}
