import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AttachmentSlot, ExcavationData, RequestDetail } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { TaskPanel } from "../components/TaskPanel";
import { useI18n } from "../i18n/I18nContext";
import { formatDate, formatDateTime, formatPlainDate, formatSize } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";
import { actionPath } from "../requests/serviceAvailability";
import { BuildingRequestView } from "./BuildingRequestView";
import { FIELD_CATEGORY, useExcavationOptions } from "../requests/useExcavationOptions";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; request: RequestDetail };

/** Message shown after arriving from another page (see the navigate() calls that set this state). */
const ARRIVAL_MESSAGES: [string, MessageKey, "notice" | "alert"][] = [
  ["submitted", "submittedSuccess", "notice"],
  ["resubmitted", "resubmittedSuccess", "notice"],
  ["lettersSubmitted", "lettersSubmittedSuccess", "notice"],
  ["buildingSubmitted", "buildingSubmitted", "notice"],
  ["changed", "requestChanged", "alert"],
];

export function RequestDetailPage() {
  const id = Number(useParams().id);
  const { t, pick, lang } = useI18n();
  const location = useLocation();
  const arrivedFrom = (location.state ?? {}) as Record<string, unknown>;
  const arrival = ARRIVAL_MESSAGES.find(([key]) => arrivedFrom[key] === true);
  const options = useExcavationOptions();
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [reloads, setReloads] = useState(0);
  const [changed, setChanged] = useState(false);

  useEffect(() => {
    let active = true;
    api
      .getRequest(id)
      .then((request) => active && setLoad({ status: "ready", request }))
      .catch((e) => active && setLoad(e instanceof ApiError && e.status === 404 ? { status: "missing" } : { status: "error" }));
    return () => {
      active = false;
    };
  }, [id, reloads]);

  const back = (
    <Link to="/requests" className="breadcrumb">
      {t("backToRequests")}
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
  const arrivalNotice = arrival && (
    <p className={arrival[2]} role={arrival[2] === "notice" ? "status" : "alert"}>
      {t(arrival[1])}
    </p>
  );
  if (r.building)
    return (
      <BuildingRequestView
        key={`${r.id}-${r.updatedAt}`}
        request={r}
        establishments={typeof options === "object" ? options.establishments : null}
        arrival={
          changed ? (
            <p className="alert" role="alert">
              {t("stageChanged")}
            </p>
          ) : (
            arrivalNotice
          )
        }
        back={back}
        reload={() => {
          setChanged(true);
          setReloads((n) => n + 1);
        }}
      />
    );
  const x = r.excavation;
  const lookupsReady = typeof options === "object";

  // Show option names when the lists have loaded; fall back to the stored code.
  const optionName = (field: keyof typeof FIELD_CATEGORY) => {
    const code = x[field];
    if (!code) return "—";
    const option = lookupsReady ? options.lookups[FIELD_CATEGORY[field]].find((o) => o.code === code) : undefined;
    return option ? pick(option, "name") : code;
  };
  const establishment = () => {
    if (x.establishmentId === null) return "—";
    const est = lookupsReady ? options.establishments.find((e) => e.id === x.establishmentId) : undefined;
    return est ? `${pick(est, "name")} – ${est.crNumber}` : String(x.establishmentId);
  };
  const metres = (v: number | null) => (v === null ? "—" : `${v} ${t("metres")}`);
  const nextStep = actionPath(r.serviceCode, r.id, r.status);
  const lettersUploaded = r.coordinationLetterSlots.some((s) => r.attachments.some((a) => a.slotCode === s.code));

  const documentList = (slots: AttachmentSlot[]) => (
    <ul className="docs">
      {slots.map((slot) => {
        const file = r.attachments.find((a) => a.slotCode === slot.code);
        return (
          <li key={slot.code}>
            {pick(slot, "label")}:{" "}
            {file ? (
              <>
                <a href={api.attachmentUrl(r.id, slot.code)} download>
                  {file.fileName}
                </a>{" "}
                <span className="meta">({formatSize(file.sizeBytes, lang)})</span>
              </>
            ) : (
              "—"
            )}
          </li>
        );
      })}
    </ul>
  );

  const sections: [string, [keyof ExcavationData, string][]][] = [
    [
      t("sectionRequest"),
      [
        ["establishmentId", establishment()],
        ["requestTypeCode", optionName("requestTypeCode")],
        ["contractTypeCode", optionName("contractTypeCode")],
        ["industrialCityCode", optionName("industrialCityCode")],
        ["permitTypeCode", optionName("permitTypeCode")],
        ["workDescription", x.workDescription ?? "—"],
      ],
    ],
    [
      t("sectionPermit"),
      [
        ["issueDate", formatPlainDate(x.issueDate)],
        ["endDate", formatPlainDate(x.endDate)],
        ["excavationMethodCode", optionName("excavationMethodCode")],
        ["workLocation", x.workLocation ?? "—"],
      ],
    ],
    [
      t("sectionDimensions"),
      [
        ["lengthM", metres(x.lengthM)],
        ["widthM", metres(x.widthM)],
        ["depthM", metres(x.depthM)],
        ["totalPathLengthM", metres(x.totalPathLengthM)],
      ],
    ],
    [
      t("sectionProjectManager"),
      [
        ["projectManagerName", x.projectManagerName ?? "—"],
        ["projectManagerPhone", x.projectManagerPhone ?? "—"],
        ["projectManagerEmail", x.projectManagerEmail ?? "—"],
        ["supervisingEntity", x.supervisingEntity ?? "—"],
      ],
    ],
  ];

  return (
    <main className="page">
      {back}
      {arrival && (
        <p className={arrival[2]} role={arrival[2] === "notice" ? "status" : "alert"}>
          {t(arrival[1])}
        </p>
      )}
      <div className="request-head">
        <div>
          <h1>{pick(r, "serviceName")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{r.requestNumber}</bdi> · {t("createdAt")}: {formatDate(r.createdAt)}
            {r.submittedAt && ` · ${t("submittedAt")}: ${formatDate(r.submittedAt)}`}
          </p>
        </div>
        <StatusBadge status={r.status} />
      </div>

      {r.currentTask ? (
        <TaskPanel task={r.currentTask}>
          {nextStep && (
            <Link className="btn primary" to={nextStep}>
              {t(`task.${r.currentTask.kind}`)}
            </Link>
          )}
        </TaskPanel>
      ) : r.canEdit && nextStep ? (
        <p>
          <Link className="btn primary" to={nextStep}>
            {t("continueEditing")}
          </Link>
        </p>
      ) : (
        <p className="muted">{t("requestLocked")}</p>
      )}

      <div className="detail">
        <section>
          {sections.map(([title, rows]) => (
            <div key={title} className="card read-section">
              <h2>{title}</h2>
              <dl className="read-grid">
                {rows.map(([field, value]) => (
                  <div key={field}>
                    <dt>{t(`field.${field}`)}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ))}

          <div className="card read-section">
            <h2>{t("sectionAttachments")}</h2>
            {documentList(r.attachmentSlots)}
          </div>

          {(lettersUploaded || r.canUploadCoordinationLetters) && (
            <div className="card read-section">
              <h2>{t("coordinationLetters")}</h2>
              {documentList(r.coordinationLetterSlots)}
            </div>
          )}
        </section>

        <aside className="card info">
          <h2>{t("statusHistory")}</h2>
          <ol className="timeline">
            {r.history.map((h, i) => (
              <li key={i}>
                <strong>{t(`action.${h.action}`)}</strong>
                <span>
                  <StatusBadge status={h.toStatus} />
                </span>
                <span className="meta">{formatDateTime(h.occurredAt)}</span>
                {h.comment && <span className="history-comment">{h.comment}</span>}
              </li>
            ))}
          </ol>
        </aside>
      </div>
    </main>
  );
}
