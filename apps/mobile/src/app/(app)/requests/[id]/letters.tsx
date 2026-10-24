import { router, useLocalSearchParams } from "expo-router";
import { useState } from "react";
import { api, ApiError } from "../../../../api/client";
import type { AttachmentInfo, FieldErrors, RequestDetail } from "../../../../api/types";
import { AttachmentField } from "../../../../components/AttachmentField";
import { useFailureText } from "../../../../components/errors";
import { useLoad } from "../../../../components/useLoad";
import { Banner, Button, Loading, Screen, Text, Title } from "../../../../components/ui";
import { useI18n } from "../../../../i18n/I18nContext";

/** The three coordination letters Modon asked for: upload, replace or remove each, then send all three. */
export default function CoordinationLettersScreen() {
  const id = Number(useLocalSearchParams<{ id: string }>().id);
  const { t } = useI18n();
  const failure = useFailureText();
  const { load } = useLoad(() => api.getRequest(id), [id]);

  if (load.status === "loading") return <Loading />;
  if (load.status === "error") return <Screen><Banner kind="error">{failure(load.error, "loadFailed")}</Banner></Screen>;
  if (!load.data.canUploadCoordinationLetters)
    return (
      <Screen>
        <Banner kind="info">{t("requestLocked")}</Banner>
        <Button label={t("viewDetails")} onPress={() => router.replace(`/requests/${id}`)} />
      </Screen>
    );
  return <Letters request={load.data} />;
}

function Letters({ request }: { request: RequestDetail }) {
  const { t } = useI18n();
  const failure = useFailureText();
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function send() {
    const uploaded = new Set(attachments.map((a) => a.slotCode));
    const missing = Object.fromEntries(request.coordinationLetterSlots.filter((s) => !uploaded.has(s.code)).map((s) => [`attachments.${s.code}`, ["required"]]));
    setErrors(missing);
    if (Object.keys(missing).length) return setError(t("lettersFailed"));
    setBusy(true);
    setError(null);
    try {
      await api.submitCoordinationLetters(request.id);
      router.replace(`/requests/${request.id}`);
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) {
        setErrors(e.errors);
        setError(t("lettersFailed"));
      } else setError(failure(e, "lettersFailed"));
      setBusy(false);
    }
  }

  return (
    <Screen>
      <Title>{t("coordinationLetters")}</Title>
      <Text muted small>{t("requestNumber")}: {request.requestNumber}</Text>
      {request.currentTask ? <Banner kind="info">{t("instructions")}: {request.currentTask.instructions ?? t("noInstructions")}</Banner> : null}
      <Text muted>{t("coordinationLettersIntro")}</Text>
      {error ? <Banner kind="error">{error}</Banner> : null}
      {request.coordinationLetterSlots.map((slot) => (
        <AttachmentField
          key={slot.code}
          requestId={request.id}
          slot={slot}
          attachment={attachments.find((a) => a.slotCode === slot.code)}
          error={errors[`attachments.${slot.code}`]?.[0]}
          onUploaded={(info) => setAttachments((list) => [...list.filter((a) => a.slotCode !== info.slotCode), info])}
          onRemoved={(code) => setAttachments((list) => list.filter((a) => a.slotCode !== code))}
        />
      ))}
      <Button label={busy ? t("submittingLetters") : t("submitLetters")} busy={busy} onPress={() => void send()} />
    </Screen>
  );
}
