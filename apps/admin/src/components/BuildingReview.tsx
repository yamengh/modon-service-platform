import { api } from "../api/client";
import type { OfficeInfo, QuotationView, ReviewRequestDetail } from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { formatDateTime, formatSize } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";

const amount = (value: number | null) => (value === null ? "—" : value.toLocaleString("en-US", { maximumFractionDigits: 2 }));

function Office({ office }: { office: OfficeInfo | null }) {
  const { pick } = useI18n();
  return office ? (
    <>
      {pick(office, "name")} <span className="meta">({office.crNumber})</span>
    </>
  ) : (
    <>—</>
  );
}

/** An office's answer: amount, comment, commitments, attachment and the consulting office's phase plan. */
function Quotation({ requestId, quotation }: { requestId: number; quotation: QuotationView }) {
  const { t } = useI18n();
  return (
    <>
      <dl className="read-grid">
        <div>
          <dt>{t("quotation.amount")}</dt>
          <dd>
            <bdi>
              {amount(quotation.amountSar)} {t("sar")}
            </bdi>
          </dd>
        </div>
        <div>
          <dt>{t("quotation.time")}</dt>
          <dd>{formatDateTime(quotation.submittedAt)}</dd>
        </div>
        {quotation.comment && (
          <div className="wide">
            <dt>{t("quotation.comment")}</dt>
            <dd className="pre">{quotation.comment}</dd>
          </div>
        )}
        {quotation.commitmentsAccepted && (
          <div>
            <dt>{t("phasePlan")}</dt>
            <dd>{t("commitmentsConfirmed")}</dd>
          </div>
        )}
        {quotation.attachment && (
          <div>
            <dt>{t("attachment")}</dt>
            <dd>
              <a href={api.buildingFileUrl(requestId, "quotations", quotation.id)} download>
                {quotation.attachment.fileName}
              </a>
              <span className="meta"> ({formatSize(quotation.attachment.sizeBytes)})</span>
            </dd>
          </div>
        )}
      </dl>
      {quotation.phases.length > 0 && (
        <table className="plain" aria-label={t("phasePlan")}>
          <thead>
            <tr>
              <th>{t("phase.name")}</th>
              <th>{t("phase.share")}</th>
              <th>{t("phase.duration")}</th>
            </tr>
          </thead>
          <tbody>
            {quotation.phases.map((p) => (
              <tr key={p.phase}>
                <td>{t(`phase.${p.phase}`)}</td>
                <td>{p.sharePercent ?? "—"}</td>
                <td>{p.durationDays ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}

/**
 * The reviewer's view of a building licence: the basic information, contacts, the chosen consulting office and
 * contractor with their quotations, every selection round, and (after approval) the monthly progress reports.
 */
export function BuildingSections({ request: r }: { request: ReviewRequestDetail }) {
  const { t, pick } = useI18n();
  const b = r.building!;
  const f = b.form;
  const basic: [MessageKey, string][] = [
    ["field.b.contractNumber", f.contractNumber ?? "—"],
    ["field.b.licenceTypeCode", f.licenceTypeCode ? (t(`licenceType.${f.licenceTypeCode}` as MessageKey) ?? f.licenceTypeCode) : "—"],
    ["field.b.constructionBudgetSar", f.constructionBudgetSar === null ? "—" : `${amount(f.constructionBudgetSar)} ${t("sar")}`],
    ["field.b.floorCount", f.floorCount === null ? "—" : String(f.floorCount)],
    ["field.b.termsAccepted", f.termsAccepted ? t("termsYes") : "—"],
    ["field.b.scopeOfWork", f.scopeOfWork ?? "—"],
    ["field.b.notes", f.notes ?? "—"],
  ];

  return (
    <>
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
          <table className="plain">
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
        <h2>{t("offices")}</h2>
        <h3 className="subhead">
          {t("selectedConsultant")}: {b.selectedConsultant ? <Office office={b.selectedConsultant} /> : t("notSelectedYet")}
        </h3>
        {b.consultantQuotation && <Quotation requestId={r.id} quotation={b.consultantQuotation} />}
        <h3 className="subhead">
          {t("selectedContractor")}: {b.selectedContractor ? <Office office={b.selectedContractor} /> : t("notSelectedYet")}
        </h3>
        {b.contractorQuotation && <Quotation requestId={r.id} quotation={b.contractorQuotation} />}
      </div>

      {b.tenders.length > 0 && (
        <div className="card read-section">
          <h2>{t("rounds")}</h2>
          <ol className="rounds">
            {b.tenders.map((round) => (
              <li key={round.id}>
                <p>
                  <strong>{t(`round.${round.stage}`)}</strong> · {t(`mode.${round.mode}`)}
                  {round.number && <> · <bdi>{round.number}</bdi></>}
                  {round.invitedOffice && <> · <Office office={round.invitedOffice} /></>} ·{" "}
                  <span className="meta">
                    {t(`roundState.${round.state}`)} · {formatDateTime(round.openedAt)}
                  </span>
                </p>
                {round.quotations.length > 0 && (
                  <table className="plain">
                    <thead>
                      <tr>
                        <th>{t("offices")}</th>
                        <th>{t("quotation.amount")}</th>
                        <th>{t("quotation.time")}</th>
                        <th>{t("status")}</th>
                        <th>{t("quotation.comment")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {round.quotations.map((q) => (
                        <tr key={q.id}>
                          <td>{pick(q.office, "name")}</td>
                          <td>
                            <bdi>{q.amountSar === null ? "—" : `${amount(q.amountSar)} ${t("sar")}`}</bdi>
                          </td>
                          <td>{formatDateTime(q.submittedAt)}</td>
                          <td>{t(`quotationState.${q.state}`)}</td>
                          <td className="pre">{q.comment ?? "—"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </li>
            ))}
          </ol>
        </div>
      )}

      {(r.status === "approved" || b.progressReports.length > 0) && (
        <div className="card read-section">
          <h2>{t("constructionProgress")}</h2>
          <ul className="progress-list">
            {b.currentProgress.map((p) => (
              <li key={p.phase}>
                <span>{t(`phase.${p.phase}`)}</span>
                <span className="progress-bar" role="progressbar" aria-label={t(`phase.${p.phase}`)} aria-valuemin={0} aria-valuemax={100}
                  aria-valuenow={p.completionPercent ?? 0}>
                  <span style={{ inlineSize: `${Math.min(100, Math.max(0, p.completionPercent ?? 0))}%` }} />
                </span>
                <bdi className="meta">{p.completionPercent ?? 0}%</bdi>
              </li>
            ))}
          </ul>
          <h3 className="subhead">{t("progressReports")}</h3>
          {b.progressReports.length === 0 && <p className="muted">{t("noReports")}</p>}
          {[...b.progressReports].reverse().map((report) => (
            <div key={report.id} className="report">
              <p>
                <strong>
                  {t("report.period")}: <bdi>{report.period}</bdi>
                </strong>{" "}
                <span className="meta">
                  · {t("report.by")}: <Office office={report.office} /> {report.submittedByEmail && <bdi>({report.submittedByEmail})</bdi>} ·{" "}
                  {formatDateTime(report.submittedAt)}
                </span>
              </p>
              <p className="report-phases">
                {report.phases.map((p) => (
                  <span key={p.phase}>
                    {t(`phase.${p.phase}`)}: <bdi>{p.completionPercent}%</bdi>
                  </span>
                ))}
              </p>
              {report.comment && <p className="history-comment">{report.comment}</p>}
              {report.attachment && (
                <p>
                  {t("attachment")}:{" "}
                  <a href={api.buildingFileUrl(r.id, "progress-reports", report.id)} download>
                    {report.attachment.fileName}
                  </a>
                </p>
              )}
            </div>
          ))}
        </div>
      )}
    </>
  );
}
