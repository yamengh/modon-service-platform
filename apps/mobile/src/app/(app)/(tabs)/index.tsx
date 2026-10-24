import { router } from "expo-router";
import { Pressable } from "react-native";
import { api } from "../../../api/client";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Empty, Loading, Screen, Text, Title } from "../../../components/ui";
import { useI18n } from "../../../i18n/I18nContext";

/** Modon services (FormCMS catalogue) — both services, as in the web portal. */
export default function ServicesScreen() {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh } = useLoad(() => api.listServices(), []);

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Title>{t("modonServices")}</Title>
      <Text muted>{t("servicesIntro")}</Text>
      {load.status === "loading" && <Loading />}
      {load.status === "error" && (
        <>
          <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
          <Button kind="secondary" label={t("retry")} onPress={refresh} />
        </>
      )}
      {load.status === "ready" && load.data.length === 0 && <Empty text={t("noServices")} />}
      {load.status === "ready" &&
        load.data.map((s) => (
          <Pressable
            key={s.code}
            accessibilityRole="button"
            accessibilityLabel={pick(s, "name")}
            onPress={() => router.push(`/services/${s.code}`)}
          >
            <Card>
              <Text bold>{pick(s, "name")}</Text>
              <Text muted small>{pick(s, "summary")}</Text>
              <Text small>
                {t("beneficiary")}: {t(`beneficiary.${s.beneficiary}`)}
              </Text>
            </Card>
          </Pressable>
        ))}
    </Screen>
  );
}
