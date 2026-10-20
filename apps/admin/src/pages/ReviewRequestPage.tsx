import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AttachmentSlot, Decision, ExcavationData, LookupCategory, ReviewRequestDetail } from "../api/types";
import { BuildingSections } from "../components/BuildingReview";
import { DecisionPanel } from "../components/DecisionPanel";
import { StatusBadge } from "../components/StatusBadge";
import { WorkflowPanel } from "../components/WorkflowPanel";
import { useI18n } from "../i18n/I18nContext";
import { formatDate, formatDateTime, formatPlainDate, formatSize } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";
import { useReferenceData } from "../reference/ReferenceData";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; request: ReviewRequestDetail };
type Notice = { kind: "success" | "warning"; key: MessageKey } | null;

const FIELD_CATEGORY: Partial<Record<keyof ExcavationData, LookupCategory>> = {
  requestTypeCode: "excavationRequestType",
  contractTypeCode: "excavationContractType",
  industrialCityCode: "industrialCity",
  permitTypeCode: "excavationPermitType",
  excavationMethodCode: "excavationMethod",
};

export function ReviewRequestPage() {
  const id = Number(useParams().id);
  const { t, pick } = useI18n();
  const { option } = useReferenceData();
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [notice, setNotice] = useState<Notice>(null);

  const fetchRequest = useCallback(
    () =>
      api
        .getRequest(id)
        .then((request) => setLoad({ status: "ready", request }))
        .catch((e) => setLoad(e instanceof ApiError && e.status === 404 ? { status: "missing" } : { status: "error" })),
    [id],
  );

  useEffect(() => {
    setLoad({ status: "loading" });
    void fetchRequest();
  }, [fetchRequest]);

  const back = (
    <Link to="/requests" className="breadcrumb">
      {t("backToQueue")}
    </Link>
  );

  if (load.status === "loading") return <main className="page">{back}<p className="status">{t("loading")}</p></main>;
  if (load.status === "missing") return <main className="page">{back}<p className="status">{t("requestNotFound")}</p></main>;
  if (load.status === "error")
    return (
      <main className="page">
        {back}
        <p className="alert" role="alert">{t("loadFailed")}</p>
      </main>
    );

  const r = load.request;
  const x = r.excavation;

  const value = (field: keyof ExcavationData): string => {
    const raw = x[field];
    if (raw === null || raw === "") return "—";
    const category = FIELD_CATEGORY[field];
    if (category) {
      const found = option(category, String(raw));
      return found ? pick(found, "name") : String(raw);
    }
    if (field === "issueDate" || field === "endDate") return formatPlainDate(String(raw));
    if (typeof raw === "number") return `${raw} ${t("metres")}`;
    return String(raw);
  };

  const sections: [MessageKey, (keyof ExcavationData)[]][] = [
    ["sectionRequest", ["requestTypeCode", "contractTypeCode", "industrialCityCode", "permitTypeCode", "workDescription"]],
    ["sectionPermit", ["issueDate", "endDate", "excavationMethodCode", "workLocation"]],
    ["sectionDimensions", ["lengthM", "widthM", "depthM", "totalPathLengthM"]],
    ["sectionProjectManager", ["projectManagerName", "projectManagerPhone", "projectManagerEmail", "supervisingEntity"]],
  ];

  // Coordination letters only appear once the applicant has uploaded at least one.
  const hasAnyFile = (slots: AttachmentSlot[]) => slots.some((s) => r.attachments.some((a) => a.slotCode === s.code));

  const documentList = (slots: AttachmentSlot[]) => (
    <ul className="docs">
      {slots.map((slot) => {
        const file = r.attachments.find((a) => a.slotCode === slot.code);
        return (
          <li key={slot.code}>
            <span className="doc-label">{pick(slot, "label")}</span>
            {file ? (
              <>
                <a href={api.attachmentUrl(r.id, slot.code)} download>
                  {file.fileName}
                </a>
                <span className="meta"> ({formatSize(file.sizeBytes)})</span>
              </>
            ) : (
              <span className="meta">{t("noFile")}</span>
            )}
          </li>
        );
      })}
    </ul>
  );

  const onDecided = (updated: ReviewRequestDetail, decision: Decision) => {
    setLoad({ status: "ready", request: updated });
    setNotice({ kind: "success", key: `done.${decision}` });
  };
  const onConflict = () => {
    setNotice({ kind: "warning", key: "decisionConflict" });
    void fetchRequest();
  };

  return (
    <main className="page">
      {back}
      {notice && (
        <p className={notice.kind === "success" ? "notice" : "alert"} role={notice.kind === "success" ? "status" : "alert"}>
          {t(notice.key)}
        </p>
      )}
      <div className="request-head">
        <div>
          <h1>{pick(r, "serviceName")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{r.requestNumber}</bdi> · {t("submittedAt")}: {formatDate(r.submittedAt)}
          </p>
        </div>
        <StatusBadge status={r.status} />
      </div>

      <div className="detail">
        <section>
          <div className="card read-section">
            <h2>{t("applicantInfo")}</h2>
            <dl className="read-grid">
              <div>
                <dt>{t("establishment")}</dt>
                <dd>{r.establishment ? pick(r.establishment, "name") : "—"}</dd>
              </div>
              <div>
                <dt>{t("crNumber")}</dt>
                <dd>{r.establishment?.crNumber ?? "—"}</dd>
              </div>
              <div>
                <dt>{t("email")}</dt>
                <dd dir="ltr">{r.applicant.email || t("unknownUser")}</dd>
              </div>
              <div>
                <dt>{t("phone")}</dt>
                <dd dir="ltr">{r.applicant.phone ?? "—"}</dd>
              </div>
            </dl>
          </div>

          {r.building && <BuildingSections request={r} />}
          {!r.building && sections.map(([title, fields]) => (
            <div key={title} className="card read-section">
              <h2>{t(title)}</h2>
              <dl className="read-grid">
                {fields.map((field) => (
                  <div key={field} className={field === "workDescription" ? "wide" : undefined}>
                    <dt>{t(`field.${field}`)}</dt>
                    <dd>{value(field)}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ))}

          <div className="card read-section">
            <h2>{t("sectionAttachments")}</h2>
            {documentList(r.attachmentSlots)}
          </div>

          {hasAnyFile(r.coordinationLetterSlots) && (
            <div className="card read-section">
              <h2>{t("coordinationLetters")}</h2>
              {documentList(r.coordinationLetterSlots)}
            </div>
          )}
        </section>

        <aside className="side">
          <div className="card">
            <h2>{t("decision")}</h2>
            {r.building && r.availableDecisions.length > 0 && <p className="hint">{t("buildingDecisionNote")}</p>}
            <DecisionPanel request={r} onDecided={onDecided} onConflict={onConflict} />
          </div>
          <WorkflowPanel key={`${r.id}-${r.status}`} request={r} />
          <div className="card">
            <h2>{t("statusHistory")}</h2>
            <ol className="timeline">
              {r.history.map((h, i) => (
                <li key={i}>
                  <strong>{t(`action.${h.action}`)}</strong>
                  <span>
                    <StatusBadge status={h.toStatus} />
                  </span>
                  <span className="meta">
                    {formatDateTime(h.occurredAt)} ·{" "}
                    {h.actorIsApplicant ? t("byApplicant") : <bdi>{h.actorEmail ?? t("unknownUser")}</bdi>}
                  </span>
                  {h.comment && <span className="history-comment">{h.comment}</span>}
                </li>
              ))}
            </ol>
          </div>
        </aside>
      </div>
    </main>
  );
}
