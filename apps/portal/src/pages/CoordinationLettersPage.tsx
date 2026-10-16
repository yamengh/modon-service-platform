import { useEffect, useState, type ReactNode } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import type { AttachmentInfo, FieldErrors, RequestDetail } from "../api/types";
import { AttachmentSlotField } from "../components/AttachmentSlotField";
import { StatusBadge } from "../components/StatusBadge";
import { TaskPanel } from "../components/TaskPanel";
import { useI18n } from "../i18n/I18nContext";

type Load = { status: "loading" } | { status: "error" } | { status: "missing" } | { status: "ready"; request: RequestDetail };

/** Upload, replace or remove the three coordination letters, then send them (all three are required). */
export function CoordinationLettersPage() {
  const id = Number(useParams().id);
  const { t } = useI18n();
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
    <Link to="/tasks" className="breadcrumb">
      {t("tasks")}
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

  // Only while Modon is waiting for the letters (the backend refuses uploads otherwise).
  if (!load.request.canUploadCoordinationLetters) return <Navigate to={`/requests/${id}`} replace />;

  return <LettersEditor request={load.request} back={back} />;
}

function LettersEditor({ request, back }: { request: RequestDetail; back: ReactNode }) {
  const { t, pick } = useI18n();
  const navigate = useNavigate();
  const slots = request.coordinationLetterSlots;
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failed, setFailed] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function send() {
    const missing: FieldErrors = Object.fromEntries(
      slots
        .filter((s) => s.requiredOnSubmit && !attachments.some((a) => a.slotCode === s.code))
        .map((s) => [`attachments.${s.code}`, ["required"]]),
    );
    setErrors(missing);
    if (Object.keys(missing).length) return setFailed(t("lettersFailed"));

    setBusy(true);
    setFailed(null);
    try {
      await api.submitCoordinationLetters(request.id);
      navigate(`/requests/${request.id}`, { state: { lettersSubmitted: true } });
    } catch (e) {
      if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) {
        setErrors(e.errors);
        setFailed(t("lettersFailed"));
      } else if (e instanceof ApiError && e.status === 409) {
        navigate(`/requests/${request.id}`, { replace: true, state: { changed: true } });
      } else {
        setFailed(t("genericError"));
      }
      setBusy(false);
    }
  }

  return (
    <main className="page">
      {back}
      <div className="request-head">
        <div>
          <h1>{t("coordinationLetters")}</h1>
          <p className="meta">
            {pick(request, "serviceName")} · {t("requestNumber")}: <bdi>{request.requestNumber}</bdi>
          </p>
        </div>
        <StatusBadge status={request.status} />
      </div>

      {request.currentTask && <TaskPanel task={request.currentTask} />}
      <p className="muted">{t("coordinationLettersIntro")}</p>

      {failed && (
        <p className="alert" role="alert">
          {failed}
        </p>
      )}

      <div className="card request-form">
        {slots.map((slot) => (
          <AttachmentSlotField
            key={slot.code}
            requestId={request.id}
            slot={slot}
            attachment={attachments.find((a) => a.slotCode === slot.code)}
            error={errors[`attachments.${slot.code}`]?.[0]}
            disabled={busy}
            onUploaded={(info) => {
              setAttachments((list) => [...list.filter((a) => a.slotCode !== info.slotCode), info]);
              setErrors(({ [`attachments.${slot.code}`]: _removed, ...rest }) => rest);
            }}
            onRemoved={(code) => setAttachments((list) => list.filter((a) => a.slotCode !== code))}
          />
        ))}
      </div>

      <div className="form-actions">
        <button type="button" className="btn primary" onClick={() => void send()} disabled={busy}>
          {busy ? t("submittingLetters") : t("submitLetters")}
        </button>
        <Link className="btn secondary" to={`/requests/${request.id}`}>
          {t("backToRequest")}
        </Link>
      </div>
    </main>
  );
}
