import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AttachmentInfo, Establishment, FieldErrors, RequestDetail } from "../api/types";
import { AttachmentSlotField } from "../components/AttachmentSlotField";
import { StatusBadge } from "../components/StatusBadge";
import { BuildingStepper } from "../components/building/BuildingStepper";
import { useI18n } from "../i18n/I18nContext";
import { errorKey, fill } from "../i18n/format";
import type { MessageKey } from "../i18n/messages";
import {
  LICENCE_TYPES,
  MAX_CONTACTS,
  REQUIRED_BUILDING_FIELDS,
  toBuildingData,
  toBuildingForm,
  validateBuilding,
  type BuildingForm,
  type ContactForm,
} from "../requests/buildingForm";

type Load =
  | { status: "loading" }
  | { status: "error" }
  | { status: "missing" }
  | { status: "ready"; request: RequestDetail; establishments: Establishment[] };
type Banner = { kind: "success" | "error"; text: string } | null;

/** Manual 1.1: the building licence's basic information, saved as a draft until it is submitted. */
export function BuildingFormPage() {
  const id = Number(useParams().id);
  const { t } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    Promise.all([api.getRequest(id), api.myEstablishments()])
      .then(([request, establishments]) => active && setLoad({ status: "ready", request, establishments }))
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
  // Submitted requests continue on the request page (consultant selection etc.).
  if (!load.request.canEdit || !load.request.building) return <Navigate to={`/requests/${id}`} replace />;
  return <BuildingEditor request={load.request} establishments={load.establishments} back={back} />;
}

function BuildingEditor({ request, establishments, back }: { request: RequestDetail; establishments: Establishment[]; back: ReactNode }) {
  const { t, pick } = useI18n();
  const navigate = useNavigate();
  const [form, setForm] = useState<BuildingForm>(() => toBuildingForm(request.building!.form));
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<Banner>(null);
  const [busy, setBusy] = useState<"save" | "submit" | null>(null);

  const set = (field: keyof Omit<BuildingForm, "contacts" | "termsAccepted">) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }));
    setErrors(({ [field]: _removed, ...rest }) => rest);
  };
  const setContact = (index: number, field: keyof ContactForm, value: string) => {
    setForm((f) => ({ ...f, contacts: f.contacts.map((c, i) => (i === index ? { ...c, [field]: value } : c)) }));
    setErrors(({ [`contacts[${index}].${field}`]: _removed, ...rest }) => rest);
  };
  // Blank contact rows are not sent, so the error indexes match the rows on screen.
  const withoutBlankContacts = (f: BuildingForm) => ({
    ...f,
    contacts: f.contacts.filter((c) => c.name.trim() || c.email.trim() || c.mobile.trim()),
  });

  const showErrors = (found: FieldErrors, failedKey: MessageKey) => {
    setErrors(found);
    const count = Object.keys(found).length;
    setBanner({ kind: "error", text: `${t(failedKey)} ${count ? fill(t("fixErrors"), { count }) : ""}`.trim() });
  };
  const handleApiError = (e: unknown, failedKey: MessageKey) => {
    if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) showErrors(e.errors, failedKey);
    else if (e instanceof ApiError && e.status === 503) setBanner({ kind: "error", text: t("workflowUnavailable") });
    else if (e instanceof ApiError && e.status === 409) navigate(`/requests/${request.id}`, { replace: true });
    else setBanner({ kind: "error", text: t("genericError") });
  };

  async function saveDraft() {
    const cleaned = withoutBlankContacts(form);
    setForm(cleaned);
    const found = validateBuilding(cleaned, "draft");
    if (Object.keys(found).length) return showErrors(found, "saveFailed");
    setBusy("save");
    try {
      await api.saveBuilding(request.id, toBuildingData(cleaned));
      setErrors({});
      setBanner({ kind: "success", text: t("draftSaved") });
    } catch (e) {
      handleApiError(e, "saveFailed");
    } finally {
      setBusy(null);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const cleaned = withoutBlankContacts(form);
    setForm(cleaned);
    const found = validateBuilding(cleaned, "submit", request.attachmentSlots, attachments);
    if (Object.keys(found).length) return showErrors(found, "submitFailed");
    setBusy("submit");
    try {
      // Save the latest edits, then submit; the backend validates both steps again and starts the workflow.
      await api.saveBuilding(request.id, toBuildingData(cleaned));
      await api.submitRequest(request.id);
      navigate(`/requests/${request.id}`, { state: { buildingSubmitted: true } });
    } catch (err) {
      handleApiError(err, "submitFailed");
      setBusy(null);
    }
  }

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
  const label = (field: keyof BuildingForm) => (
    <span>
      {(REQUIRED_BUILDING_FIELDS as readonly string[]).includes(field) && <span className="req">* </span>}
      {t(`field.b.${field}` as MessageKey)}
    </span>
  );

  return (
    <main className="page">
      {back}
      <div className="request-head">
        <div>
          <h1>{t("buildingTitle")}</h1>
          <p className="meta">
            {t("requestNumber")}: <bdi>{request.requestNumber}</bdi>
          </p>
        </div>
        <StatusBadge status={request.status} />
      </div>
      <BuildingStepper status={request.status} />
      <p className="muted">{t("requiredHint")}</p>
      {banner && (
        <p className={banner.kind === "success" ? "notice" : "alert"} role={banner.kind === "success" ? "status" : "alert"}>
          {banner.text}
        </p>
      )}

      <form className="request-form" onSubmit={(e) => void submit(e)} noValidate>
        <fieldset className="card">
          <legend>{t("sectionBasicInfo")}</legend>
          <div className="grid-2">
            <label className="field">
              {label("establishmentId")}
              <select value={form.establishmentId} onChange={(e) => set("establishmentId")(e.target.value)} {...a11y("establishmentId")}>
                <option value="">{t("choose")}</option>
                {establishments.map((est) => (
                  <option key={est.id} value={String(est.id)}>
                    {pick(est, "name")} – {est.crNumber}
                  </option>
                ))}
              </select>
              {establishments.length === 0 && <small className="hint warn">{t("noEstablishments")}</small>}
              {fieldError("establishmentId")}
            </label>
            <label className="field">
              {label("contractNumber")}
              <input dir="ltr" value={form.contractNumber} onChange={(e) => set("contractNumber")(e.target.value)} {...a11y("contractNumber")} />
              <small className="hint">{t("contractNumberHint")}</small>
              {fieldError("contractNumber")}
            </label>
            <label className="field">
              {label("licenceTypeCode")}
              <select value={form.licenceTypeCode} onChange={(e) => set("licenceTypeCode")(e.target.value)} {...a11y("licenceTypeCode")}>
                <option value="">{t("choose")}</option>
                {LICENCE_TYPES.map((code) => (
                  <option key={code} value={code}>
                    {t(`licenceType.${code}`)}
                  </option>
                ))}
              </select>
              {fieldError("licenceTypeCode")}
            </label>
            <label className="field">
              {label("constructionBudgetSar")}
              <span className="with-unit">
                <input
                  type="number"
                  dir="ltr"
                  inputMode="decimal"
                  min="0"
                  step="any"
                  value={form.constructionBudgetSar}
                  onChange={(e) => set("constructionBudgetSar")(e.target.value)}
                  {...a11y("constructionBudgetSar")}
                />
                <span className="unit">{t("sar")}</span>
              </span>
              {fieldError("constructionBudgetSar")}
            </label>
            <label className="field">
              {label("floorCount")}
              <input
                type="number"
                dir="ltr"
                inputMode="numeric"
                min="1"
                step="1"
                value={form.floorCount}
                onChange={(e) => set("floorCount")(e.target.value)}
                {...a11y("floorCount")}
              />
              {fieldError("floorCount")}
            </label>
          </div>
          <div className="grid-2">
            <label className="field">
              {label("scopeOfWork")}
              <textarea rows={4} value={form.scopeOfWork} onChange={(e) => set("scopeOfWork")(e.target.value)} {...a11y("scopeOfWork")} />
              {fieldError("scopeOfWork")}
            </label>
            <label className="field">
              {label("notes")}
              <textarea rows={4} value={form.notes} onChange={(e) => set("notes")(e.target.value)} {...a11y("notes")} />
              {fieldError("notes")}
            </label>
          </div>
        </fieldset>

        <fieldset className="card">
          <legend>{t("contacts")}</legend>
          <h3 className="subhead">{t("contactDetails")}</h3>
          {form.contacts.length === 0 && <p className="muted">{t("noContacts")}</p>}
          {form.contacts.map((c, i) => (
            <div key={i} className="contact-row" data-testid="contact-row">
              {(["name", "email", "mobile"] as const).map((field) => (
                <label key={field} className="field">
                  <span>{t(`contact.${field}`)}</span>
                  <input
                    type={field === "email" ? "email" : field === "mobile" ? "tel" : "text"}
                    dir={field === "name" ? undefined : "ltr"}
                    value={c[field]}
                    onChange={(e) => setContact(i, field, e.target.value)}
                    {...a11y(`contacts[${i}].${field}`)}
                  />
                  {fieldError(`contacts[${i}].${field}`)}
                </label>
              ))}
              <button
                type="button"
                className="link-btn danger"
                onClick={() => setForm((f) => ({ ...f, contacts: f.contacts.filter((_, j) => j !== i) }))}
              >
                {t("removeContact")}
              </button>
            </div>
          ))}
          {form.contacts.length < MAX_CONTACTS && (
            <button
              type="button"
              className="btn secondary small"
              onClick={() => setForm((f) => ({ ...f, contacts: [...f.contacts, { name: "", email: "", mobile: "" }] }))}
            >
              {t("addContact")}
            </button>
          )}
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

        <div className="card terms">
          <label className="checkbox">
            <input
              type="checkbox"
              checked={form.termsAccepted}
              onChange={(e) => {
                setForm((f) => ({ ...f, termsAccepted: e.target.checked }));
                setErrors(({ termsAccepted: _removed, ...rest }) => rest);
              }}
              {...a11y("termsAccepted")}
            />
            <span>
              <span className="req">* </span>
              {t("termsAccepted")}
              <small className="hint">{t("termsHint")}</small>
            </span>
          </label>
          {fieldError("termsAccepted")}
        </div>

        <div className="form-actions">
          <button type="submit" className="btn primary" disabled={busy !== null}>
            {busy === "submit" ? t("submitting") : t("submitRequest")}
          </button>
          <button type="button" className="btn secondary" onClick={() => void saveDraft()} disabled={busy !== null}>
            {busy === "save" ? t("savingDraft") : t("saveDraft")}
          </button>
        </div>
      </form>
    </main>
  );
}
