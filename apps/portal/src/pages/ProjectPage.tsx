import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { FieldErrors, OfficeDecision, PartnerProjectDetail, PhasePlan, PhaseProgress } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { PHASES, ProgressBars, QuotationSummary, ReportList } from "../components/building/BuildingParts";
import { useI18n } from "../i18n/I18nContext";
import { errorKey, formatAmount, formatDate, formatSize } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";
import { checkFile } from "../requests/excavationForm";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; project: PartnerProjectDetail };

const OFFICE_FILE = {
  code: "officeAttachment",
  labelAr: "مرفق عام",
  labelEn: "General attachment",
  requiredOnSubmit: false,
  maxSizeKb: 10240,
  extensions: [".jpeg", ".jpg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"],
};

/** "yyyy-MM" of the current month. */
const thisMonth = () => new Date().toISOString().slice(0, 7);

/** A building project as an office sees it: quote (open project), answer (invited), report (awarded consulting office). */
export function ProjectPage() {
  const id = Number(useParams().id);
  const { t } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [notice, setNotice] = useState<MessageKey | null>(null);

  useEffect(() => {
    let active = true;
    api
      .getProject(id)
      .then((project) => active && setLoad({ status: "ready", project }))
      .catch((e) => active && setLoad(e instanceof ApiError && e.status === 404 ? { status: "missing" } : { status: "error" }));
    return () => {
      active = false;
    };
  }, [id]);

  const back = (
    <Link to="/projects" className="breadcrumb">
      {t("backToProjects")}
    </Link>
  );
  if (load.status === "loading") return <main className="page">{back}<p className="status">{t("loading")}</p></main>;
  if (load.status === "missing") return <main className="page">{back}<p className="status">{t("projectNotFound")}</p></main>;
  if (load.status === "error")
    return (
      <main className="page">
        {back}
        <p className="alert" role="alert">{t("loadFailed")}</p>
      </main>
    );

  const p = load.project;
  const updated = (project: PartnerProjectDetail, key: MessageKey) => {
    setLoad({ status: "ready", project });
    setNotice(key);
  };

  return (
    <main className="page">
      {back}
      {notice && (
        <p className="notice" role="status">
          {t(notice)}
        </p>
      )}
      <div className="request-head">
        <div>
          <h1>{t("buildingTitle")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{p.requestNumber}</bdi>
            {p.currentRound?.number && (
              <>
                {" "}
                · {t("projectNumber")}: <bdi>{p.currentRound.number}</bdi>
              </>
            )}{" "}
            · <span className="badge">{t(`relation.${p.relation}`)}</span>
          </p>
        </div>
        <StatusBadge status={p.status} />
      </div>

      {p.canQuote && <QuoteForm project={p} onSaved={(project) => updated(project, "quoteSubmitted")} />}
      {p.canRespond && <RespondForm project={p} onSaved={(project) => updated(project, "answerSent")} />}
      {p.canReport && <ReportForm project={p} onSaved={(project) => updated(project, "reportSubmitted")} />}

      <div className="detail">
        <section>
          <ProjectDetails project={p} />
          {p.myQuotations.length > 0 && (
            <div className="card read-section">
              <h2>{t("myQuotations")}</h2>
              {p.myQuotations.map((q) => (
                <QuotationSummary key={q.id} quotation={q} fileUrl={(qid) => api.projectFileUrl(p.requestId, "quotations", qid)} />
              ))}
            </div>
          )}
          {p.consultantPlan && (
            <div className="card read-section">
              <h2>{t("selectedConsultant")}</h2>
              <QuotationSummary quotation={p.consultantPlan} fileUrl={(qid) => api.projectFileUrl(p.requestId, "quotations", qid)} />
            </div>
          )}
          {p.relation === "awarded" && (
            <div className="card read-section">
              <h2>{t("constructionProgress")}</h2>
              <ProgressBars progress={p.currentProgress} />
              <h3 className="subhead">{t("progressReports")}</h3>
              <ReportList reports={p.progressReports} fileUrl={(rid) => api.projectFileUrl(p.requestId, "progress-reports", rid)} />
            </div>
          )}
        </section>
      </div>
    </main>
  );
}

function ProjectDetails({ project: p }: { project: PartnerProjectDetail }) {
  const { t, pick, lang } = useI18n();
  const rows: [MessageKey, string][] = [
    ["investorEstablishment", p.investor ? `${pick(p.investor, "name")} – ${p.investor.crNumber}` : "—"],
    ["field.b.licenceTypeCode", p.licenceTypeCode ? t(`licenceType.${p.licenceTypeCode}` as MessageKey) ?? p.licenceTypeCode : "—"],
    ["field.b.constructionBudgetSar", p.constructionBudgetSar === null ? "—" : `${formatAmount(p.constructionBudgetSar)} ${t("sar")}`],
    ["field.b.floorCount", p.floorCount === null ? "—" : String(p.floorCount)],
    ["submittedAt", formatDate(p.submittedAt)],
    ["field.b.scopeOfWork", p.scopeOfWork ?? "—"],
    ["field.b.notes", p.notes ?? "—"],
  ];
  return (
    <div className="card read-section">
      <h2>{t("projectDetails")}</h2>
      <dl className="read-grid">
        {rows.map(([key, value]) => (
          <div key={key} className={key === "field.b.scopeOfWork" || key === "field.b.notes" ? "wide" : undefined}>
            <dt>{t(key)}</dt>
            <dd className="pre">{value}</dd>
          </div>
        ))}
      </dl>
      {p.attachmentSlots.length > 0 && (
        <>
          <h3 className="subhead">{t("sectionAttachments")}</h3>
          <ul className="docs">
            {p.attachmentSlots.map((slot) => {
              const file = p.attachments.find((a) => a.slotCode === slot.code);
              return (
                <li key={slot.code}>
                  {pick(slot, "label")}:{" "}
                  {file ? (
                    <>
                      <a href={api.projectAttachmentUrl(p.requestId, slot.code)} download>
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
        </>
      )}
    </div>
  );
}

/** Shared handling: field errors (400), workflow unavailable (503), project changed (409). */
function useSubmit() {
  const { t } = useI18n();
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function run(action: () => Promise<void>) {
    setFailure(null);
    setBusy(true);
    try {
      await action();
      setErrors({});
    } catch (e) {
      if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) setErrors(e.errors);
      else if (e instanceof ApiError && e.status === 503) setFailure(t("workflowUnavailable"));
      else if (e instanceof ApiError && e.status === 409 && ["quotationAlreadySubmitted", "reportPeriodTaken"].includes(e.message))
        setFailure(t(errorKey(e.message)));
      else if (e instanceof ApiError && (e.status === 409 || e.status === 404)) setFailure(t("stageChanged"));
      else setFailure(t("genericError"));
    } finally {
      setBusy(false);
    }
  }
  const error = (field: string) =>
    errors[field]?.[0] && (
      <small className="error" role="alert">
        {t(errorKey(errors[field][0]))}
      </small>
    );
  return { errors, setErrors, failure, busy, run, error };
}

function AmountInput({ value, onChange, invalid, label }: { value: string; onChange: (v: string) => void; invalid: boolean; label: string }) {
  const { t } = useI18n();
  return (
    <span className="with-unit">
      <input type="number" dir="ltr" inputMode="decimal" min="0" step="any" value={value} aria-label={label}
        onChange={(e) => onChange(e.target.value)} aria-invalid={invalid} />
      <span className="unit">{t("sar")}</span>
    </span>
  );
}

/** Manual 1.3 / 1.6: office, amount, comment, "تقديم". */
function QuoteForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t, pick } = useI18n();
  const id = useId();
  const quotedOffices = new Set(project.myQuotations.filter((q) => q.tenderId === project.currentRound?.id).map((q) => q.office.id));
  const offices = project.myOffices.filter((o) => !quotedOffices.has(o.id));
  const [officeId, setOfficeId] = useState(offices.length === 1 ? String(offices[0].id) : "");
  const [amount, setAmount] = useState("");
  const [comment, setComment] = useState("");
  const { errors, setErrors, failure, busy, run, error } = useSubmit();

  function submit(e: FormEvent) {
    e.preventDefault();
    const found: FieldErrors = {};
    if (!officeId) found.establishmentId = ["required"];
    if (!amount.trim()) found.amountSar = ["required"];
    else if (!(Number(amount) > 0)) found.amountSar = ["mustBePositive"];
    if (Object.keys(found).length) return setErrors(found);
    void run(async () => onSaved(await api.submitQuotation(project.requestId, Number(officeId), Number(amount), comment.trim() || null)));
  }

  return (
    <form className="card stage-panel" onSubmit={submit} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t("quoteTitle")}</h2>
      <label className="field">
        <span>
          <span className="req">* </span>
          {t(`quoteOffice.${project.stage}`)}
        </span>
        <select value={officeId} onChange={(e) => setOfficeId(e.target.value)} aria-invalid={!!errors.establishmentId}>
          <option value="">{t("choose")}</option>
          {offices.map((o) => (
            <option key={o.id} value={String(o.id)}>
              {pick(o, "name")} – {o.crNumber}
            </option>
          ))}
        </select>
        {error("establishmentId")}
      </label>
      <label className="field">
        <span>
          <span className="req">* </span>
          {t("quotation.amount")}
        </span>
        <AmountInput value={amount} onChange={setAmount} invalid={!!errors.amountSar} label={t("quotation.amount")} />
        {error("amountSar")}
      </label>
      <label className="field">
        <span>{t("quotation.comment")}</span>
        <textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
        {error("comment")}
      </label>
      {failure && <p className="alert" role="alert">{failure}</p>}
      <button type="submit" className="btn primary" disabled={busy}>
        {t("submitQuote")}
      </button>
    </form>
  );
}

/** Manual 1.4 / 1.7: attachment, accept or reject; accepting needs the amount (and, for a consulting office, the phases and commitments). */
function RespondForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const id = useId();
  const consultant = project.stage === "consultant";
  const fileRef = useRef<HTMLInputElement>(null);
  const [decision, setDecision] = useState<OfficeDecision | null>(null);
  const [amount, setAmount] = useState("");
  const [comment, setComment] = useState("");
  const [commitments, setCommitments] = useState(false);
  const [phases, setPhases] = useState<Record<string, { share: string; days: string }>>(
    Object.fromEntries(PHASES.map((p) => [p, { share: "", days: "" }])),
  );
  const [file, setFile] = useState<File | null>(null);
  const { errors, setErrors, failure, busy, run, error } = useSubmit();

  function submit(e: FormEvent) {
    e.preventDefault();
    const found: FieldErrors = {};
    if (!decision) found.decision = ["required"];
    if (decision === "reject" && !comment.trim()) found.comment = ["required"];
    if (decision === "accept") {
      if (!amount.trim()) found.amountSar = ["required"];
      else if (!(Number(amount) > 0)) found.amountSar = ["mustBePositive"];
      if (consultant) {
        if (!commitments) found.commitmentsAccepted = ["required"];
        PHASES.forEach((p, i) => {
          const share = phases[p].share.trim();
          const days = phases[p].days.trim();
          if (!share) found[`phases[${i}].sharePercent`] = ["required"];
          else if (!(Number(share) >= 0 && Number(share) <= 100)) found[`phases[${i}].sharePercent`] = ["outOfRange"];
          if (!days) found[`phases[${i}].durationDays`] = ["required"];
          else if (!Number.isInteger(Number(days)) || Number(days) < 1) found[`phases[${i}].durationDays`] = ["mustBePositive"];
        });
      }
    }
    if (file) {
      const problem = checkFile(file, OFFICE_FILE);
      if (problem) found.file = [problem];
    }
    if (Object.keys(found).length) return setErrors(found);

    const plan: PhasePlan[] | null =
      decision === "accept" && consultant
        ? PHASES.map((p) => ({ phase: p, sharePercent: Number(phases[p].share), durationDays: Number(phases[p].days) }))
        : null;
    void run(async () => {
      const result = await api.respondToInvitation(
        project.requestId,
        {
          decision: decision!,
          amountSar: decision === "accept" ? Number(amount) : null,
          comment: comment.trim() || null,
          commitmentsAccepted: decision === "accept" && commitments,
          phases: plan,
        },
        decision === "accept" ? file : null,
      );
      if (result) onSaved(result);
      else navigate("/projects", { replace: true, state: { declined: true } });
    });
  }

  return (
    <form className="card stage-panel" onSubmit={submit} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t("quoteTitle")}</h2>
      <div className="field">
        <span>{t(`officeAttachment.${project.stage}`)}</span>
        <small className="hint">{t("officeAttachmentHint")}</small>
        <input ref={fileRef} type="file" accept={OFFICE_FILE.extensions.join(",")} aria-label={t(`officeAttachment.${project.stage}`)}
          onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        {error("file")}
      </div>
      <div className="radio-row" role="radiogroup" aria-label={t("quoteTitle")}>
        {(["accept", "reject"] as const).map((d) => (
          <label key={d} className="radio">
            <input type="radio" name={`${id}-decision`} checked={decision === d} onChange={() => setDecision(d)} />
            {t(`decision.${d}`)}
          </label>
        ))}
      </div>
      {error("decision")}

      {decision === "accept" && consultant && (
        <>
          <h3 className="subhead">{t("phasePlan")}</h3>
          <div className="table-wrap">
            <table className="requests phases">
              <thead>
                <tr>
                  <th>{t("phase.name")}</th>
                  <th>{t("phase.share")}</th>
                  <th>{t("phase.duration")}</th>
                </tr>
              </thead>
              <tbody>
                {PHASES.map((p, i) => (
                  <tr key={p}>
                    <td>{t(`phase.${p}`)}</td>
                    <td>
                      <input type="number" dir="ltr" min="0" max="100" step="any" aria-label={`${t(`phase.${p}`)} – ${t("phase.share")}`}
                        value={phases[p].share} onChange={(e) => setPhases((x) => ({ ...x, [p]: { ...x[p], share: e.target.value } }))}
                        aria-invalid={!!errors[`phases[${i}].sharePercent`]} />
                      {error(`phases[${i}].sharePercent`)}
                    </td>
                    <td>
                      <input type="number" dir="ltr" min="1" step="1" aria-label={`${t(`phase.${p}`)} – ${t("phase.duration")}`}
                        value={phases[p].days} onChange={(e) => setPhases((x) => ({ ...x, [p]: { ...x[p], days: e.target.value } }))}
                        aria-invalid={!!errors[`phases[${i}].durationDays`]} />
                      {error(`phases[${i}].durationDays`)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {error("phases")}
          <label className="checkbox">
            <input type="checkbox" checked={commitments} onChange={(e) => setCommitments(e.target.checked)} aria-invalid={!!errors.commitmentsAccepted} />
            <span>
              <span className="req">* </span>
              {t("commitmentsAccepted")}
            </span>
          </label>
          {error("commitmentsAccepted")}
        </>
      )}

      {decision === "accept" && (
        <label className="field">
          <span>
            <span className="req">* </span>
            {t("quotation.amount")}
          </span>
          <AmountInput value={amount} onChange={setAmount} invalid={!!errors.amountSar} label={t("quotation.amount")} />
          {error("amountSar")}
        </label>
      )}
      {decision && (
        <label className="field">
          <span>
            {decision === "reject" && <span className="req">* </span>}
            {t("quotation.comment")}
          </span>
          <textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} aria-invalid={!!errors.comment} />
          {error("comment")}
        </label>
      )}
      {failure && <p className="alert" role="alert">{failure}</p>}
      <button type="submit" className="btn primary" disabled={busy}>
        {t("submitQuote")}
      </button>
    </form>
  );
}

/** A monthly construction-progress report: month, the six phases' completion (0–100 %), comment, optional attachment. */
function ReportForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t } = useI18n();
  const id = useId();
  const [period, setPeriod] = useState(thisMonth());
  const [values, setValues] = useState<Record<string, string>>(
    Object.fromEntries(project.currentProgress.map((p) => [p.phase, String(p.completionPercent ?? 0)])),
  );
  const [comment, setComment] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const { errors, setErrors, failure, busy, run, error } = useSubmit();

  function submit(e: FormEvent) {
    e.preventDefault();
    const found: FieldErrors = {};
    if (!/^\d{4}-\d{2}$/.test(period)) found.period = ["required"];
    PHASES.forEach((p, i) => {
      const v = (values[p] ?? "").trim();
      if (!v) found[`phases[${i}].completionPercent`] = ["required"];
      else if (!(Number(v) >= 0 && Number(v) <= 100)) found[`phases[${i}].completionPercent`] = ["outOfRange"];
    });
    if (file) {
      const problem = checkFile(file, OFFICE_FILE);
      if (problem) found.file = [problem];
    }
    if (Object.keys(found).length) return setErrors(found);
    const phases: PhaseProgress[] = PHASES.map((p) => ({ phase: p, completionPercent: Number(values[p]) }));
    void run(async () => onSaved(await api.submitProgressReport(project.requestId, { period, phases, comment: comment.trim() || null }, file)));
  }

  return (
    <form className="card stage-panel" onSubmit={submit} noValidate aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t("newReport")}</h2>
      <p className="hint">{t("reportHint")}</p>
      <label className="field">
        <span>
          <span className="req">* </span>
          {t("report.period")}
        </span>
        <input type="month" dir="ltr" value={period} max={thisMonth()} onChange={(e) => setPeriod(e.target.value)} aria-invalid={!!errors.period} />
        {error("period")}
      </label>
      <div className="table-wrap">
        <table className="requests phases">
          <thead>
            <tr>
              <th>{t("phase.name")}</th>
              <th>{t("report.completion")}</th>
            </tr>
          </thead>
          <tbody>
            {PHASES.map((p, i) => (
              <tr key={p}>
                <td>{t(`phase.${p}`)}</td>
                <td>
                  <input type="number" dir="ltr" min="0" max="100" step="any" aria-label={`${t(`phase.${p}`)} – ${t("report.completion")}`}
                    value={values[p] ?? ""} onChange={(e) => setValues((x) => ({ ...x, [p]: e.target.value }))}
                    aria-invalid={!!errors[`phases[${i}].completionPercent`]} />
                  {error(`phases[${i}].completionPercent`)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {error("phases")}
      <label className="field">
        <span>{t("quotation.comment")}</span>
        <textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
        {error("comment")}
      </label>
      <div className="field">
        <span>{t("attachment")}</span>
        <small className="hint">{t("officeAttachmentHint")}</small>
        <input type="file" accept={OFFICE_FILE.extensions.join(",")} aria-label={t("attachment")} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        {error("file")}
      </div>
      {failure && <p className="alert" role="alert">{failure}</p>}
      <button type="submit" className="btn primary" disabled={busy}>
        {t("submitReport")}
      </button>
    </form>
  );
}
