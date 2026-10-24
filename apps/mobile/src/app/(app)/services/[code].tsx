import { router, useLocalSearchParams } from "expo-router";
import { useState } from "react";
import { api } from "../../../api/client";
import { useUser } from "../../../auth/AuthContext";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Detail, Empty, Loading, Screen, Text, Title } from "../../../components/ui";
import { useI18n } from "../../../i18n/I18nContext";
import { canStart, formRoute, isOpenService } from "../../../requests/routes";

const lines = (text: string) => text.split("\n").map((l) => l.trim()).filter(Boolean);

export default function ServiceDetailScreen() {
  const { code = "" } = useLocalSearchParams<{ code: string }>();
  const { t, pick } = useI18n();
  const user = useUser();
  const failure = useFailureText();
  const { load } = useLoad(() => api.getService(code), [code]);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  if (load.status === "loading") return <Loading />;
  if (load.status === "error") return <Screen><Banner kind="error">{failure(load.error, "loadFailed")}</Banner></Screen>;
  if (!load.data) return <Screen><Empty text={t("serviceNotFound")} /></Screen>;
  const s = load.data;
  const allowed = canStart(s.code, user.personas);

  async function start() {
    setStarting(true);
    setStartError(null);
    try {
      const draft = await api.createRequest(s.code);
      router.replace(formRoute(s.code, draft.id));
    } catch (e) {
      setStartError(failure(e, "startFailed"));
      setStarting(false);
    }
  }

  return (
    <Screen>
      <Title>{pick(s, "name")}</Title>
      <Text>{pick(s, "description") || pick(s, "summary")}</Text>
      <Card>
        <Detail label={t("beneficiary")} value={t(`beneficiary.${s.beneficiary}`)} />
        <Detail label={t("duration")} value={pick(s, "duration") || t("notSpecified")} />
        <Detail label={t("channel")} value={pick(s, "channel") || t("notSpecified")} />
        <Detail label={t("cost")} value={pick(s, "cost") || t("notSpecified")} />
      </Card>
      <Title level={2}>{t("steps")}</Title>
      {lines(pick(s, "steps")).map((step, i) => (
        <Text key={step}>
          {i + 1}. {step}
        </Text>
      ))}
      <Title level={2}>{t("requiredDocuments")}</Title>
      {lines(pick(s, "requiredDocuments")).map((doc) => (
        <Text key={doc}>• {doc}</Text>
      ))}
      {startError ? <Banner kind="error">{startError}</Banner> : null}
      {!isOpenService(s.code) && <Text muted>{t("startServiceSoon")}</Text>}
      {isOpenService(s.code) && !allowed && <Text muted>{t("serviceForOtherPersona")}</Text>}
      <Button label={starting ? t("startingService") : t("startService")} onPress={() => void start()} disabled={!allowed} busy={starting} />
    </Screen>
  );
}
