import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AttachmentInfo, ExcavationData, FieldErrors, RequestDetail } from "../api/types";
import { AttachmentSlotField } from "../components/AttachmentSlotField";
import { StatusBadge } from "../components/StatusBadge";
import { TaskPanel } from "../components/TaskPanel";
import { useI18n } from "../i18n/I18nContext";
import { errorKey, fill } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";
import {
  DIMENSION_FIELDS,
  REQUIRED_FIELDS,
  toData,
  toForm,
  validateExcavation,
  type ExcavationForm,
} from "../requests/excavationForm";
import { FIELD_CATEGORY, useExcavationOptions, type ExcavationOptions } from "../requests/useExcavationOptions";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; request: RequestDetail };
type Banner = { kind: "success" | "error"; text: string } | null;

export function ExcavationFormPage() {
  const id = Number(useParams().id);
  const { t } = useI18n();
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

  if (load.status === "missing") return <main className="page">{back}<p className="status">{t("requestNotFound")}</p></main>;
  if (load.status === "error" || options === "error")
    return (
      <main className="page">
        {back}
        <p className="alert" role="alert">{t("loadFailed")}</p>
      </main>
    );
  if (load.status === "loading" || options === "loading")
    return <main className="page">{back}<p className="status">{t("loading")}</p></main>;

  // Submitted requests are read-only: show the details page instead.
  if (!load.request.canEdit) return <Navigate to={`/requests/${id}`} replace />;

  return <ExcavationEditor request={load.request} options={options} back={back} />;
}

function ExcavationEditor({ request, options, back }: { request: RequestDetail; options: ExcavationOptions; back: ReactNode }) {
  const { t, pick } = useI18n();
  const navigate = useNavigate();
  const [form, setForm] = useState<ExcavationForm>(() => toForm(request.excavation));
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<Banner>(null);
  const [busy, setBusy] = useState<"save" | "submit" | null>(null);
  // A request Modon returned for completion: same form, but it is resubmitted, and the establishment
  // it was submitted for cannot change (the backend enforces both).
  const returned = request.status === "returnedForCompletion";
  const establishmentLocked = request.submittedAt !== null;

  const set = (field: keyof ExcavationData) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }));
    setErrors(({ [field]: _removed, ...rest }) => rest);
  };

  const showErrors = (found: FieldErrors, failedKey: MessageKey) => {
    setErrors(found);
    const count = Object.keys(found).length;
    setBanner({ kind: "error", text: `${t(failedKey)} ${count ? fill(t("fixErrors"), { count }) : ""}`.trim() });
  };

  const handleApiError = (e: unknown, failedKey: MessageKey) => {
    if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) showErrors(e.errors, failedKey);
    else if (e instanceof ApiError && e.status === 503) setBanner({ kind: "error", text: t("workflowUnavailable") });
    else if (e instanceof ApiError && e.status === 409 && e.message === "workflowNotStarted")
      setBanner({ kind: "error", text: t("workflowNotStarted") });
    else if (e instanceof ApiError && e.status === 409) navigate(`/requests/${request.id}`, { replace: true });
    else setBanner({ kind: "error", text: t("genericError") });
  };

  async function saveDraft() {
    const found = validateExcavation(form, "draft");
    if (Object.keys(found).length) return showErrors(found, "saveFailed");
    setBusy("save");
    try {
      await api.saveExcavation(request.id, toData(form));
      setErrors({});
      setBanner({ kind: "success", text: t(returned ? "changesSaved" : "draftSaved") });
    } catch (e) {
      handleApiError(e, "saveFailed");
    } finally {
      setBusy(null);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const failedKey = returned ? "resubmitFailed" : "submitFailed";
    const found = validateExcavation(form, "submit", request.attachmentSlots, attachments);
    if (Object.keys(found).length) return showErrors(found, failedKey);
    setBusy("submit");
    try {
      // Save the latest edits first, then submit; the backend validates both steps again.
      await api.saveExcavation(request.id, toData(form));
      await api.submitRequest(request.id);
      navigate(`/requests/${request.id}`, { state: returned ? { resubmitted: true } : { submitted: true } });
    } catch (err) {
      handleApiError(err, failedKey);
      setBusy(null);
    }
  }

  const label = (field: keyof ExcavationData) => (
    <span>
      {REQUIRED_FIELDS.includes(field) && <span className="req">* </span>}
      {t(`field.${field}`)}
    </span>
  );
  const fieldError = (field: string) =>
    errors[field]?.[0] && (
      <small className="error" id={`${field}-error`}>
        {t(errorKey(errors[field][0]))}
      </small>
    );
  const a11y = (field: string) => ({
    "aria-invalid": !!errors[field],
    "aria-describedby": errors[field] ? `${field}-error` : undefined,
  });

  const select = (field: keyof typeof FIELD_CATEGORY) => (
    <label className="field">
      {label(field)}
      <select value={form[field]} onChange={(e) => set(field)(e.target.value)} {...a11y(field)}>
        <option value="">{t("choose")}</option>
        {options.lookups[FIELD_CATEGORY[field]].map((o) => (
          <option key={o.code} value={o.code}>
            {pick(o, "name")}
          </option>
        ))}
      </select>
      {fieldError(field)}
    </label>
  );

  const input = (field: keyof ExcavationData, type: "text" | "date" | "email" | "tel" | "number" = "text") => (
    <label className="field">
      {label(field)}
      <input
        type={type}
        dir={type === "text" ? undefined : "ltr"}
        inputMode={type === "number" ? "decimal" : undefined}
        step={type === "number" ? "any" : undefined}
        value={form[field]}
        onChange={(e) => set(field)(e.target.value)}
        {...a11y(field)}
      />
      {fieldError(field)}
    </label>
  );

  return (
    <main className="page">
      {back}
      <div className="request-head">
        <div>
          <h1>{t("excavationTitle")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{request.requestNumber}</bdi>
          </p>
        </div>
        <StatusBadge status={request.status} />
      </div>
      {request.currentTask && (
        <TaskPanel task={request.currentTask}>
          <p className="hint">{t("returnedHint")}</p>
        </TaskPanel>
      )}
      <p className="muted">{t("requiredHint")}</p>

      {banner && (
        <p className={banner.kind === "success" ? "notice" : "alert"} role={banner.kind === "success" ? "status" : "alert"}>
          {banner.text}
        </p>
      )}

      <form className="request-form" onSubmit={(e) => void submit(e)} noValidate>
        <fieldset className="card">
          <legend>{t("sectionRequest")}</legend>
          <div className="grid-2">
            <label className="field">
              {label("establishmentId")}
              <select
                value={form.establishmentId}
                onChange={(e) => set("establishmentId")(e.target.value)}
                disabled={establishmentLocked}
                {...a11y("establishmentId")}
              >
                <option value="">{t("choose")}</option>
                {options.establishments.map((est) => (
                  <option key={est.id} value={String(est.id)}>
                    {pick(est, "name")} – {est.crNumber}
                  </option>
                ))}
              </select>
              {options.establishments.length === 0 && <small className="hint warn">{t("noEstablishments")}</small>}
              {establishmentLocked && <small className="hint">{t("establishmentLocked")}</small>}
              {fieldError("establishmentId")}
            </label>
            {select("requestTypeCode")}
            {select("contractTypeCode")}
            {select("industrialCityCode")}
            {select("permitTypeCode")}
          </div>
          <label className="field">
            {label("workDescription")}
            <textarea rows={4} value={form.workDescription} onChange={(e) => set("workDescription")(e.target.value)} {...a11y("workDescription")} />
            {fieldError("workDescription")}
          </label>
        </fieldset>

        <fieldset className="card">
          <legend>{t("sectionPermit")}</legend>
          <div className="grid-2">
            {input("issueDate", "date")}
            {input("endDate", "date")}
            {select("excavationMethodCode")}
            {input("workLocation")}
          </div>
        </fieldset>

        <fieldset className="card">
          <legend>{t("sectionDimensions")}</legend>
          <div className="grid-2">{DIMENSION_FIELDS.map((f) => <div key={f}>{input(f, "number")}</div>)}</div>
        </fieldset>

        <fieldset className="card">
          <legend>{t("sectionProjectManager")}</legend>
          <div className="grid-2">
            {input("projectManagerName")}
            {input("projectManagerPhone", "tel")}
            {input("projectManagerEmail", "email")}
            {input("supervisingEntity")}
          </div>
        </fieldset>

        <fieldset className="card">
          <legend>{t("sectionAttachments")}</legend>
          {request.attachmentSlots.map((slot) => (
            <AttachmentSlotField
              key={slot.code}
              requestId={request.id}
              slot={slot}
              attachment={attachments.find((a) => a.slotCode === slot.code)}
              error={errors[`attachments.${slot.code}`]?.[0]}
              onUploaded={(info) => {
                setAttachments((list) => [...list.filter((a) => a.slotCode !== info.slotCode), info]);
                setErrors(({ [`attachments.${slot.code}`]: _removed, ...rest }) => rest);
              }}
              onRemoved={(code) => setAttachments((list) => list.filter((a) => a.slotCode !== code))}
            />
          ))}
        </fieldset>

        <div className="form-actions">
          <button type="submit" className="btn primary" disabled={busy !== null}>
            {busy === "submit" ? t(returned ? "resubmitting" : "submitting") : t(returned ? "resubmitRequest" : "submitRequest")}
          </button>
          <button type="button" className="btn secondary" onClick={() => void saveDraft()} disabled={busy !== null}>
            {busy === "save" ? t("savingDraft") : t(returned ? "saveChanges" : "saveDraft")}
          </button>
        </div>
      </form>
    </main>
  );
}
