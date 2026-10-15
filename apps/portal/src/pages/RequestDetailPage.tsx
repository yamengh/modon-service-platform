import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { ExcavationData, RequestDetail } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { useI18n } from "../i18n/I18nContext";
import { formatDate, formatDateTime, formatPlainDate, formatSize } from "../i18n/format";
import { OPEN_SERVICES } from "../requests/serviceAvailability";
import { FIELD_CATEGORY, useExcavationOptions } from "../requests/useExcavationOptions";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; request: RequestDetail };

export function RequestDetailPage() {
  const id = Number(useParams().id);
  const { t, pick, lang } = useI18n();
  const location = useLocation();
  const justSubmitted = (location.state as { submitted?: boolean } | null)?.submitted === true;
  const options = useExcavationOptions();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    api
      .getRequest(id)
      .then((request) => active && setLoad({ status: "ready", request }))
      .catch((e) => active && setLoad(e instanceof ApiError && e.status === 404 ? { status: "missing" } : { status: "error" }));
    return () => {
      active = false;
    };
  }, [id]);

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
      {justSubmitted && (
        <p className="notice" role="status">
          {t("submittedSuccess")}
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

      {r.canEdit && OPEN_SERVICES[r.serviceCode] ? (
        <p>
          <Link className="btn primary" to={OPEN_SERVICES[r.serviceCode].formPath(r.id)}>
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
            <ul className="docs">
              {r.attachmentSlots.map((slot) => {
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
          </div>
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
              </li>
            ))}
          </ol>
        </aside>
      </div>
    </main>
  );
}
