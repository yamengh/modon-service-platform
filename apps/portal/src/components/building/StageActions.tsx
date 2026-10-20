import { useEffect, useId, useState } from "react";
import { api, ApiError } from "../../api/client";
import type { BuildingStage, OfficeInfo, RequestDetail, SelectionMode, TenderView } from "../../api/types";
import { useI18n } from "../../i18n/I18nContext";
import { errorKey, formatDateTime } from "../../i18n/format";
import { Amount } from "./BuildingParts";

interface ActionProps {
  request: RequestDetail;
  stage: BuildingStage;
  onUpdated: (request: RequestDetail) => void;
  /** The request changed underneath (409): the page reloads it. */
  onConflict: () => void;
}

/** Turns a failed stage action into a message, or reports a conflict to the page. */
function useActionError(onConflict: () => void) {
  const { t } = useI18n();
  const [failure, setFailure] = useState<string | null>(null);
  const handle = (e: unknown): Record<string, string[]> => {
    if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) return e.errors;
    if (e instanceof ApiError && e.status === 503) setFailure(t("workflowUnavailable"));
    else if (e instanceof ApiError && e.status === 409 && e.message === "workflowNotStarted") setFailure(t("workflowNotStarted"));
    else if (e instanceof ApiError && e.status === 409) onConflict();
    else setFailure(t("genericError"));
    return {};
  };
  return { failure, setFailure, handle };
}

/** Manual 1.2 (and the contractor stage): "المشروع" or a specific office, then "التقدم في الطلب". */
export function ChooseOfficePanel({ request, stage, onUpdated, onConflict }: ActionProps) {
  const { t, pick } = useI18n();
  const selectId = useId();
  const [mode, setMode] = useState<SelectionMode>("openTender");
  const [officeId, setOfficeId] = useState("");
  const [offices, setOffices] = useState<OfficeInfo[] | null>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { failure, setFailure, handle } = useActionError(onConflict);

  useEffect(() => {
    if (mode !== "direct" || offices) return;
    let active = true;
    api
      .listOffices(stage)
      .then((list) => active && setOffices(list))
      .catch(() => active && setOffices([]));
    return () => {
      active = false;
    };
  }, [mode, offices, stage]);

  async function proceed() {
    setFailure(null);
    if (mode === "direct" && !officeId) return setFieldError("required");
    setBusy(true);
    try {
      onUpdated(await api.chooseOffice(request.id, stage, mode, mode === "direct" ? Number(officeId) : undefined));
    } catch (e) {
      const errors = handle(e);
      if (errors.establishmentId) setFieldError(errors.establishmentId[0]);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card stage-panel" aria-labelledby={`${selectId}-title`}>
      <h2 id={`${selectId}-title`}>{t(`chooseTitle.${stage}`)}</h2>
      <div className="radio-row" role="radiogroup" aria-label={t(`chooseTitle.${stage}`)}>
        {(["openTender", "direct"] as const).map((m) => (
          <label key={m} className="radio">
            <input type="radio" name={`${selectId}-mode`} checked={mode === m} onChange={() => setMode(m)} />
            {m === "openTender" ? t("mode.openTender") : t(`mode.direct.${stage}`)}
          </label>
        ))}
      </div>
      <p className="hint">{t(mode === "openTender" ? "modeHint.openTender" : "modeHint.direct")}</p>
      {mode === "direct" && (
        <label className="field" htmlFor={selectId}>
          <span>
            <span className="req">* </span>
            {t(`office.${stage}`)}
          </span>
          <select
            id={selectId}
            value={officeId}
            onChange={(e) => {
              setOfficeId(e.target.value);
              setFieldError(null);
            }}
            aria-invalid={!!fieldError}
          >
            <option value="">{offices ? t("choose") : t("loading")}</option>
            {(offices ?? []).map((o) => (
              <option key={o.id} value={String(o.id)}>
                {pick(o, "name")} – {o.crNumber}
              </option>
            ))}
          </select>
          {fieldError && <small className="error">{t(errorKey(fieldError))}</small>}
        </label>
      )}
      {failure && (
        <p className="alert" role="alert">
          {failure}
        </p>
      )}
      <button type="button" className="btn primary" onClick={() => void proceed()} disabled={busy}>
        {busy ? t("proceeding") : t("proceed")}
      </button>
    </section>
  );
}

/** Manual 1.5: the investor picks one quotation ("إختيار عرض سعر") or rejects all / cancels the project. */
export function QuotationsPanel({ request, stage, round, onUpdated, onConflict }: ActionProps & { round: TenderView }) {
  const { t, pick } = useI18n();
  const groupId = useId();
  const [chosen, setChosen] = useState<number | null>(null);
  const [rejecting, setRejecting] = useState(false);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const { failure, setFailure, handle } = useActionError(onConflict);
  const pending = round.quotations.filter((q) => q.state === "submitted");

  async function run(action: () => Promise<RequestDetail>) {
    setFailure(null);
    setBusy(true);
    try {
      onUpdated(await action());
    } catch (e) {
      handle(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card stage-panel" aria-labelledby={`${groupId}-title`}>
      <h2 id={`${groupId}-title`}>{t("quotations")}</h2>
      {round.number && (
        <p className="meta">
          {t("projectNumber")}: <bdi>{round.number}</bdi>
        </p>
      )}
      {pending.length === 0 ? (
        <p className="muted">{t("noQuotationsYet")}</p>
      ) : (
        <div className="table-wrap">
          <table className="requests">
            <thead>
              <tr>
                <th aria-label={t("chooseQuotation")} />
                <th>{t("quotation.office")}</th>
                <th>{t("quotation.time")}</th>
                <th>{t("quotation.amount")}</th>
                <th>{t("quotation.comment")}</th>
              </tr>
            </thead>
            <tbody>
              {pending.map((q) => (
                <tr key={q.id}>
                  <td>
                    <input
                      type="radio"
                      name={`${groupId}-quotation`}
                      checked={chosen === q.id}
                      onChange={() => setChosen(q.id)}
                      aria-label={pick(q.office, "name")}
                    />
                  </td>
                  <td>{pick(q.office, "name")}</td>
                  <td>{formatDateTime(q.submittedAt)}</td>
                  <td>
                    <Amount value={q.amountSar} />
                  </td>
                  <td className="instructions">{q.comment ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {failure && (
        <p className="alert" role="alert">
          {failure}
        </p>
      )}
      {rejecting ? (
        <div className="decision-form">
          <label className="field">
            <span>{t("rejectAllComment")}</span>
            <textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
          </label>
          <div className="form-actions">
            <button
              type="button"
              className="btn danger"
              disabled={busy}
              onClick={() => void run(() => api.rejectQuotations(request.id, stage, comment.trim()))}
            >
              {t("confirmRejectAll")}
            </button>
            <button type="button" className="btn secondary" onClick={() => setRejecting(false)} disabled={busy}>
              {t("cancel")}
            </button>
          </div>
        </div>
      ) : (
        <div className="form-actions">
          <button
            type="button"
            className="btn primary"
            disabled={busy || chosen === null}
            onClick={() => chosen !== null && void run(() => api.acceptQuotation(request.id, stage, chosen))}
          >
            {t("chooseQuotation")}
          </button>
          <button type="button" className="btn secondary" onClick={() => setRejecting(true)} disabled={busy}>
            {t("rejectAll")}
          </button>
        </div>
      )}
    </section>
  );
}
