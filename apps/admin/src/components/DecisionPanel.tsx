import { useId, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import type { Decision, ReviewRequestDetail } from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { errorKey } from "../i18n/format";

const MAX_LENGTH = 2000;

/** Return needs a comment and reject needs a reason (the backend enforces the same rule). */
const TEXT_REQUIRED: Record<Decision, boolean> = { approve: false, returnForCompletion: true, reject: true };

interface Props {
  request: ReviewRequestDetail;
  onDecided: (updated: ReviewRequestDetail, decision: Decision) => void;
  /** Called when the status changed underneath us (409): the page reloads the request. */
  onConflict: () => void;
}

export function DecisionPanel({ request, onDecided, onConflict }: Props) {
  const { t } = useI18n();
  const textId = useId();
  const [decision, setDecision] = useState<Decision | null>(null);
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (request.availableDecisions.length === 0) return <p className="muted">{t("noDecisions")}</p>;

  const choose = (d: Decision) => {
    setDecision(d);
    setText("");
    setError(null);
    setFailure(null);
  };

  async function confirm(e: FormEvent) {
    e.preventDefault();
    if (!decision) return;
    const value = text.trim();
    if (TEXT_REQUIRED[decision] && !value) return setError("required");
    if (value.length > MAX_LENGTH) return setError("tooLong");

    setBusy(true);
    setFailure(null);
    try {
      const updated =
        decision === "approve"
          ? await api.approve(request.id, value || undefined)
          : decision === "reject"
            ? await api.reject(request.id, value)
            : await api.returnForCompletion(request.id, value);
      setDecision(null);
      onDecided(updated, decision);
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setDecision(null);
        onConflict();
      } else if (err instanceof ApiError && err.status === 400) {
        setError(Object.values(err.errors)[0]?.[0] ?? "required");
      } else {
        setFailure(t("decisionFailed"));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="decision">
      <p className="muted">{t("decisionIntro")}</p>
      <div className="decision-buttons" role="group" aria-label={t("decision")}>
        {request.availableDecisions.map((d) => (
          <button
            key={d}
            type="button"
            className={`btn ${d === "approve" ? "primary" : d === "reject" ? "danger" : "warn"} ${decision === d ? "selected" : ""}`}
            aria-pressed={decision === d}
            onClick={() => choose(d)}
            disabled={busy}
          >
            {t(`decide.${d}`)}
          </button>
        ))}
      </div>

      {decision && (
        <form className="decision-form" onSubmit={(e) => void confirm(e)} noValidate>
          <label className="field" htmlFor={textId}>
            <span>
              {TEXT_REQUIRED[decision] && <span className="req">* </span>}
              {t(`label.${decision}`)}
            </span>
            <textarea
              id={textId}
              rows={4}
              value={text}
              onChange={(e) => {
                setText(e.target.value);
                setError(null);
              }}
              aria-invalid={!!error}
              aria-describedby={error ? `${textId}-error` : undefined}
            />
            {error && (
              <small className="error" id={`${textId}-error`}>
                {t(errorKey(error))}
              </small>
            )}
          </label>
          {decision === "returnForCompletion" && <p className="hint">{t("returnNote")}</p>}
          {failure && (
            <p className="alert" role="alert">
              {failure}
            </p>
          )}
          <div className="form-actions">
            <button type="submit" className="btn primary" disabled={busy}>
              {busy ? t("confirming") : t("confirm")}
            </button>
            <button type="button" className="btn secondary" onClick={() => setDecision(null)} disabled={busy}>
              {t("cancel")}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
