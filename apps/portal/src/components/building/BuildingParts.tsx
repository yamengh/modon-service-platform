import type {
  ConstructionPhase,
  OfficeInfo,
  PhasePlan,
  PhaseProgress,
  ProgressReportView,
  QuotationView,
  RequestStatus,
} from "../../api/types";
import { useI18n } from "../../i18n/I18nContext";
import { formatAmount, formatDateTime, formatPeriod, formatSize } from "../../i18n/format";

/** The six construction phases, in the manual's order. */
export const PHASES: ConstructionPhase[] = [
  "sitePreparation",
  "foundations",
  "structuralWorks",
  "wallsInsulationFloors",
  "electricalMechanicalArchitectural",
  "siteFinishingAndFence",
];

export function OfficeName({ office }: { office: OfficeInfo | null }) {
  const { pick } = useI18n();
  if (!office) return <>—</>;
  return (
    <>
      {pick(office, "name")} <span className="meta">({office.crNumber})</span>
    </>
  );
}

export function Amount({ value }: { value: number | null }) {
  const { t } = useI18n();
  return (
    <bdi>
      {formatAmount(value)} {value !== null && t("sar")}
    </bdi>
  );
}

/** The accepting consulting office's phase table: share of the project and duration. */
export function PhasePlanTable({ phases }: { phases: PhasePlan[] }) {
  const { t } = useI18n();
  if (phases.length === 0) return null;
  return (
    <table className="requests phases">
      <thead>
        <tr>
          <th>{t("phase.name")}</th>
          <th>{t("phase.share")}</th>
          <th>{t("phase.duration")}</th>
        </tr>
      </thead>
      <tbody>
        {phases.map((p) => (
          <tr key={p.phase}>
            <td>{t(`phase.${p.phase}`)}</td>
            <td>{p.sharePercent ?? "—"}</td>
            <td>{p.durationDays ?? "—"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Completion per phase, as bars. */
export function ProgressBars({ progress }: { progress: PhaseProgress[] }) {
  const { t } = useI18n();
  return (
    <ul className="progress-list">
      {progress.map((p) => (
        <li key={p.phase}>
          <span>{t(`phase.${p.phase}`)}</span>
          <span className="progress-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={p.completionPercent ?? 0}
            aria-label={t(`phase.${p.phase}`)}>
            <span style={{ inlineSize: `${Math.min(100, Math.max(0, p.completionPercent ?? 0))}%` }} />
          </span>
          <bdi className="meta">{p.completionPercent ?? 0}%</bdi>
        </li>
      ))}
    </ul>
  );
}

/** The monthly reports, newest first, each with its phase percentages. */
export function ReportList({ reports, fileUrl }: { reports: ProgressReportView[]; fileUrl: (reportId: number) => string }) {
  const { t, lang } = useI18n();
  if (reports.length === 0) return <p className="muted">{t("noReports")}</p>;
  return (
    <ol className="report-list">
      {[...reports].reverse().map((r) => (
        <li key={r.id} className="report">
          <p>
            <strong>
              {t("report.period")}: <bdi>{formatPeriod(r.period)}</bdi>
            </strong>{" "}
            <span className="meta">
              · {t("report.by")}: <OfficeName office={r.office} /> {r.submittedByEmail && <bdi>({r.submittedByEmail})</bdi>} ·{" "}
              {formatDateTime(r.submittedAt)}
            </span>
          </p>
          <p className="report-phases">
            {r.phases.map((p) => (
              <span key={p.phase}>
                {t(`phase.${p.phase}`)}: <bdi>{p.completionPercent}%</bdi>
              </span>
            ))}
          </p>
          {r.comment && <p className="history-comment">{r.comment}</p>}
          {r.attachment && (
            <p>
              {t("attachment")}:{" "}
              <a href={fileUrl(r.id)} download>
                {r.attachment.fileName}
              </a>{" "}
              <span className="meta">({formatSize(r.attachment.sizeBytes, lang)})</span>
            </p>
          )}
        </li>
      ))}
    </ol>
  );
}

/** One office's answer: amount, comment, commitments, attachment and (consulting office) phase plan. */
export function QuotationSummary({ quotation, fileUrl }: { quotation: QuotationView; fileUrl: (id: number) => string }) {
  const { t, lang } = useI18n();
  return (
    <div className="quotation">
      <dl className="read-grid">
        <div>
          <dt>{t("quotation.office")}</dt>
          <dd>
            <OfficeName office={quotation.office} />
          </dd>
        </div>
        <div>
          <dt>{t("quotation.amount")}</dt>
          <dd>
            <Amount value={quotation.amountSar} />
          </dd>
        </div>
        <div>
          <dt>{t("quotation.time")}</dt>
          <dd>{formatDateTime(quotation.submittedAt)}</dd>
        </div>
        <div>
          <dt>{t("quotation.state")}</dt>
          <dd>{t(`quotationState.${quotation.state}`)}</dd>
        </div>
        {quotation.comment && (
          <div className="wide">
            <dt>{t("quotation.comment")}</dt>
            <dd className="pre">{quotation.comment}</dd>
          </div>
        )}
        {quotation.commitmentsAccepted && (
          <div>
            <dt>{t("commitmentsAccepted")}</dt>
            <dd>{t("commitmentsConfirmed")}</dd>
          </div>
        )}
        {quotation.attachment && (
          <div>
            <dt>{t("attachment")}</dt>
            <dd>
              <a href={fileUrl(quotation.id)} download>
                {quotation.attachment.fileName}
              </a>{" "}
              <span className="meta">({formatSize(quotation.attachment.sizeBytes, lang)})</span>
            </dd>
          </div>
        )}
      </dl>
      {quotation.phases.length > 0 && (
        <>
          <h3 className="subhead">{t("phasePlan")}</h3>
          <PhasePlanTable phases={quotation.phases} />
        </>
      )}
    </div>
  );
}

/** Where the request is in the manual's stages. */
export function stageIndex(status: RequestStatus): number {
  switch (status) {
    case "draft":
      return 0;
    case "consultantSelection":
    case "consultantTender":
    case "awaitingConsultantDecision":
      return 1;
    case "contractorSelection":
    case "contractorTender":
    case "awaitingContractorDecision":
      return 2;
    case "underModonReview":
    case "rejected":
      return 3;
    case "approved":
      return 4;
    default:
      return 0;
  }
}
