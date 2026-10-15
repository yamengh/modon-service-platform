import { useId, useRef, useState, type ChangeEvent } from "react";
import { api, ApiError } from "../api/client";
import type { AttachmentInfo, AttachmentSlot } from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { errorKey, fill, formatSize } from "../i18n/format";
import { checkFile } from "../requests/excavationForm";

interface Props {
  requestId: number;
  slot: AttachmentSlot;
  attachment?: AttachmentInfo;
  /** Error code from the parent form (e.g. "required" after a failed submit). */
  error?: string;
  disabled?: boolean;
  onUploaded: (info: AttachmentInfo) => void;
  onRemoved: (slotCode: string) => void;
}

/** One upload slot: checks the file locally, uploads it, and shows/replaces/removes it. */
export function AttachmentSlotField({ requestId, slot, attachment, error, disabled, onUploaded, onRemoved }: Props) {
  const { t, lang, pick } = useI18n();
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const shownError = localError ?? error;

  async function onChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;

    const problem = checkFile(file, slot);
    if (problem) {
      setLocalError(problem);
      return;
    }
    setLocalError(null);
    setBusy(true);
    try {
      onUploaded(await api.uploadAttachment(requestId, slot.code, file));
    } catch (err) {
      const code = err instanceof ApiError ? err.errors[`attachments.${slot.code}`]?.[0] : undefined;
      setLocalError(code ?? "uploadFailed");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true);
    try {
      await api.deleteAttachment(requestId, slot.code);
      onRemoved(slot.code);
    } catch {
      setLocalError("uploadFailed");
    } finally {
      setBusy(false);
    }
  }

  const message = shownError === "uploadFailed" ? t("uploadFailed") : shownError ? t(errorKey(shownError)) : null;

  return (
    <div className="attachment" data-slot={slot.code}>
      <label htmlFor={inputId} className="attachment-label">
        {slot.requiredOnSubmit && <span className="req">* </span>}
        {pick(slot, "label")}
      </label>
      <p className="hint">
        {fill(t("attachmentRules"), { size: slot.maxSizeKb, ext: slot.extensions.map((x) => x.slice(1)).join(", ") })}
      </p>

      {attachment && (
        <div className="attachment-file">
          <a href={api.attachmentUrl(requestId, slot.code)} download>
            {attachment.fileName}
          </a>
          <span className="meta">{formatSize(attachment.sizeBytes, lang)}</span>
          {!disabled && (
            <button type="button" className="link-btn danger" onClick={() => void remove()} disabled={busy}>
              {t("removeAttachment")}
            </button>
          )}
        </div>
      )}

      {!disabled && (
        <>
          <input
            ref={inputRef}
            id={inputId}
            type="file"
            className="visually-hidden"
            accept={slot.extensions.join(",")}
            onChange={(e) => void onChange(e)}
            disabled={busy}
            aria-invalid={!!message}
          />
          <button type="button" className="btn secondary small" onClick={() => inputRef.current?.click()} disabled={busy}>
            {busy ? t("uploading") : attachment ? t("replaceAttachment") : t("addAttachment")}
          </button>
        </>
      )}
      {message && (
        <small className="error" role="alert">
          {message}
        </small>
      )}
    </div>
  );
}
