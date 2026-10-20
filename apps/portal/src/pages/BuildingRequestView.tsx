import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { BuildingStage, Establishment, RequestDetail, TenderView } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { TaskPanel } from "../components/TaskPanel";
import { OfficeName, ProgressBars, QuotationSummary, ReportList } from "../components/building/BuildingParts";
import { BuildingStepper } from "../components/building/BuildingStepper";
import { ChooseOfficePanel, QuotationsPanel } from "../components/building/StageActions";
import { useI18n } from "../i18n/I18nContext";
import { fill, formatAmount, formatDate, formatDateTime, formatSize } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";

interface Props {
  request: RequestDetail;
  establishments: Establishment[] | null;
  arrival: ReactNode;
  back: ReactNode;
  /** Reloads the request from the backend (after a conflict). */
  reload: () => void;
}

/** The investor's building-licence request: stage actions, details, selected offices, progress and history. */
export function BuildingRequestView({ request, establishments, arrival, back, reload }: Props) {
  const { t, pick, lang } = useI18n();
  const [r, setR] = useState(request);
  const b = r.building!;
  const f = b.form;
  const can = (action: string) => b.investorActions.includes(action as never);

  const onUpdated = (updated: RequestDetail) => setR(updated);
  const onConflict = () => reload();
  const openRound = (stage: BuildingStage): TenderView | undefined =>
    b.tenders.filter((x) => x.stage === stage && x.state === "open").at(-1);

  const stagePanels = (["consultant", "contractor"] as const).map((stage) => {
    const prefix = stage === "consultant" ? "Consultant" : "Contractor";
    if (can(`open${prefix}Tender`) || can(`select${prefix}`))
      return <ChooseOfficePanel key={stage} request={r} stage={stage} onUpdated={onUpdated} onConflict={onConflict} />;
    const round = openRound(stage);
    if (round && can(`accept${prefix}Quotation`))
      return <QuotationsPanel key={stage} request={r} stage={stage} round={round} onUpdated={onUpdated} onConflict={onConflict} />;
    if (round?.mode === "direct")
      return (
        <p key={stage} className="card waiting">
          {fill(t("waitingForOffice"), { office: round.invitedOffice ? pick(round.invitedOffice, "name") : t(`office.${stage}`) })}
        </p>
      );
    return null;
  });

  const establishment = () => {
    if (f.establishmentId === null) return "—";
    const est = establishments?.find((e) => e.id === f.establishmentId);
    return est ? `${pick(est, "name")} – ${est.crNumber}` : String(f.establishmentId);
  };
  const basic: [MessageKey, ReactNode][] = [
    ["field.b.establishmentId", establishment()],
    ["field.b.contractNumber", f.contractNumber ? <bdi>{f.contractNumber}</bdi> : "—"],
    ["field.b.licenceTypeCode", f.licenceTypeCode ? t(`licenceType.${f.licenceTypeCode}` as MessageKey) ?? f.licenceTypeCode : "—"],
    ["field.b.constructionBudgetSar", f.constructionBudgetSar === null ? "—" : `${formatAmount(f.constructionBudgetSar)} ${t("sar")}`],
    ["field.b.floorCount", f.floorCount ?? "—"],
    ["field.b.scopeOfWork", f.scopeOfWork ?? "—"],
    ["field.b.notes", f.notes ?? "—"],
  ];
  const approved = r.status === "approved";

  return (
    <main className="page">
      {back}
      {arrival}
      <div className="request-head">
        <div>
          <h1>{t("buildingTitle")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{r.requestNumber}</bdi> · {t("createdAt")}: {formatDate(r.createdAt)}
            {r.submittedAt && ` · ${t("submittedAt")}: ${formatDate(r.submittedAt)}`}
          </p>
        </div>
        <StatusBadge status={r.status} />
      </div>
      <BuildingStepper status={r.status} />

      {r.canEdit && (
        <p>
          <Link className="btn primary" to={`/requests/${r.id}/building`}>
            {t("continueEditing")}
          </Link>
        </p>
      )}
      {r.currentTask && r.currentTask.instructions && <TaskPanel task={r.currentTask} />}
      {stagePanels}
      {r.status === "underModonReview" && <p className="card waiting">{t("underReviewNote")}</p>}

      <div className="detail">
        <section>
          <div className="card read-section">
            <h2>{t("sectionBasicInfo")}</h2>
            <dl className="read-grid">
              {basic.map(([key, value]) => (
                <div key={key} className={key === "field.b.scopeOfWork" || key === "field.b.notes" ? "wide" : undefined}>
                  <dt>{t(key)}</dt>
                  <dd className="pre">{value}</dd>
                </div>
              ))}
            </dl>
          </div>

          <div className="card read-section">
            <h2>{t("contacts")}</h2>
            {f.contacts.length === 0 ? (
              <p className="muted">{t("noContacts")}</p>
            ) : (
              <table className="requests">
                <thead>
                  <tr>
                    <th>{t("contact.name")}</th>
                    <th>{t("contact.email")}</th>
                    <th>{t("contact.mobile")}</th>
                  </tr>
                </thead>
                <tbody>
                  {f.contacts.map((c, i) => (
                    <tr key={i}>
                      <td>{c.name}</td>
                      <td dir="ltr">{c.email}</td>
                      <td dir="ltr">{c.mobile}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

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

          {(b.consultantQuotation || b.contractorQuotation) && (
            <div className="card read-section">
              {b.consultantQuotation && (
                <>
                  <h2>{t("selectedConsultant")}</h2>
                  <QuotationSummary quotation={b.consultantQuotation} fileUrl={(id) => api.buildingFileUrl(r.id, "quotations", id)} />
                </>
              )}
              {b.contractorQuotation && (
                <>
                  <h2>{t("selectedContractor")}</h2>
                  <QuotationSummary quotation={b.contractorQuotation} fileUrl={(id) => api.buildingFileUrl(r.id, "quotations", id)} />
                </>
              )}
            </div>
          )}

          {(approved || b.progressReports.length > 0) && (
            <div className="card read-section">
              <h2>{t("constructionProgress")}</h2>
              <ProgressBars progress={b.currentProgress} />
              <h3 className="subhead">{t("progressReports")}</h3>
              <ReportList reports={b.progressReports} fileUrl={(id) => api.buildingFileUrl(r.id, "progress-reports", id)} />
            </div>
          )}

          {b.tenders.length > 0 && (
            <div className="card read-section">
              <h2>{t("rounds")}</h2>
              <ol className="rounds">
                {b.tenders.map((round) => (
                  <li key={round.id}>
                    <p>
                      <strong>{t(`round.${round.stage}`)}</strong> · {round.mode === "openTender" ? t("mode.openTender") : t(`mode.direct.${round.stage}`)}
                      {round.number && (
                        <>
                          {" "}
                          · <bdi>{round.number}</bdi>
                        </>
                      )}
                      {round.invitedOffice && (
                        <>
                          {" "}
                          · <OfficeName office={round.invitedOffice} />
                        </>
                      )}{" "}
                      · <span className="meta">{t(`roundState.${round.state}`)} · {formatDateTime(round.openedAt)}</span>
                    </p>
                    {round.quotations.length > 0 && (
                      <ul className="docs">
                        {round.quotations.map((q) => (
                          <li key={q.id}>
                            {pick(q.office, "name")}: {q.amountSar !== null ? `${formatAmount(q.amountSar)} ${t("sar")}` : "—"} ·{" "}
                            {t(`quotationState.${q.state}`)}
                            {q.comment && <span className="history-comment">{q.comment}</span>}
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ol>
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
