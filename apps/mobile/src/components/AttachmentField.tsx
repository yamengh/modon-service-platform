import * as DocumentPicker from "expo-document-picker";
import { useState } from "react";
import { View } from "react-native";
import { checkFile } from "@portal/requests/excavationForm";
import { fill, formatSize } from "@portal/i18n/format";
import { api, ApiError, files } from "../api/client";
import type { AttachmentInfo, AttachmentSlot, PickedFile } from "../api/types";
import { useErrorText, useI18n } from "../i18n/I18nContext";
import { Button, Card, Row, Text, colors } from "./ui";

/** MIME types the device's picker should offer for a slot's extensions (the backend checks again). */
const PICKER_TYPES: Record<string, string> = {
  ".jpeg": "image/jpeg",
  ".jpg": "image/jpeg",
  ".png": "image/png",
  ".pdf": "application/pdf",
  ".doc": "application/msword",
  ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  ".xls": "application/vnd.ms-excel",
  ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
};

/** Opens the system document picker; returns null when cancelled. */
export async function pickFile(extensions: string[]): Promise<PickedFile | null> {
  const types = [...new Set(extensions.map((e) => PICKER_TYPES[e]).filter(Boolean))];
  const result = await DocumentPicker.getDocumentAsync({ type: types.length ? types : "*/*", copyToCacheDirectory: true, multiple: false });
  if (result.canceled || !result.assets?.length) return null;
  const a = result.assets[0];
  return { uri: a.uri, name: a.name, size: a.size ?? null, mimeType: a.mimeType ?? null };
}

/** Same checks as the web portal before uploading: not empty, within the size limit, an allowed extension. */
export function checkPicked(file: PickedFile, slot: AttachmentSlot): string | null {
  return checkFile({ name: file.name, size: file.size ?? 1 } as File, slot);
}

interface Props {
  requestId: number;
  slot: AttachmentSlot;
  attachment?: AttachmentInfo;
  /** Error code from the form (e.g. "required" after a failed submit). */
  error?: string;
  disabled?: boolean;
  onUploaded: (info: AttachmentInfo) => void;
  onRemoved: (slotCode: string) => void;
}

/** One upload slot: pick, check, upload with the session token; shows the uploaded file; retry after a failure. */
export function AttachmentField({ requestId, slot, attachment, error, disabled, onUploaded, onRemoved }: Props) {
  const { t, pick, lang } = useI18n();
  const errorText = useErrorText();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState<{ file: PickedFile; message: string } | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const shown = localError ? errorText(localError) : failed?.message ?? (error ? errorText(error) : null);

  async function upload(file: PickedFile) {
    const problem = checkPicked(file, slot);
    if (problem) {
      setFailed(null);
      return setLocalError(problem);
    }
    setLocalError(null);
    setBusy(true);
    try {
      onUploaded(await api.uploadAttachment(requestId, slot.code, file));
      setFailed(null);
    } catch (e) {
      const code = e instanceof ApiError ? e.errors[`attachments.${slot.code}`]?.[0] : undefined;
      const message = code ? errorText(code) : e instanceof ApiError && e.offline ? t("networkError") : t("uploadFailed");
      setFailed({ file, message });
    } finally {
      setBusy(false);
    }
  }

  async function choose() {
    const file = await pickFile(slot.extensions);
    if (file) await upload(file);
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

  return (
    <Card style={{ gap: 8 }}>
      <Text bold>
        {slot.requiredOnSubmit ? <Text style={{ color: colors.danger }}>* </Text> : null}
        {pick(slot, "label")}
      </Text>
      <Text muted small>
        {fill(t("attachmentRules"), { size: slot.maxSizeKb, ext: slot.extensions.map((x) => x.slice(1)).join(", ") })}
      </Text>
      {attachment ? (
        <Row>
          <Text style={{ flexShrink: 1 }} accessibilityLabel={`${t("uploadedFile")}: ${attachment.fileName}`}>
            ✓ {attachment.fileName} ({formatSize(attachment.sizeBytes, lang)})
          </Text>
        </Row>
      ) : (
        <Text muted small>{t("noFileYet")}</Text>
      )}
      {shown ? (
        <Text small style={{ color: colors.danger }} accessibilityRole="alert">
          {shown}
        </Text>
      ) : null}
      {!disabled && (
        <View style={{ gap: 8 }}>
          {failed ? <Button kind="secondary" label={t("uploadRetry")} onPress={() => void upload(failed.file)} busy={busy} /> : null}
          <Button kind="secondary" label={attachment ? t("replaceFile") : t("pickFile")} onPress={() => void choose()} busy={busy && !failed} />
          {attachment ? <Button kind="secondary" label={t("removeAttachment")} onPress={() => void remove()} disabled={busy} /> : null}
        </View>
      )}
      {attachment ? (
        <Button kind="secondary" label={t("openFile")} onPress={() => void api.openProtectedFile(files.attachment(requestId, slot.code), attachment.fileName).catch(() => setLocalError("uploadFailed"))} />
      ) : null}
    </Card>
  );
}
