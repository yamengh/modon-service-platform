import { router } from "expo-router";
import { Pressable } from "react-native";
import { formatDate } from "@portal/i18n/format";
import { api } from "../../../api/client";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Empty, Loading, Row, Screen, StatusBadge, Text, Title } from "../../../components/ui";
import { useI18n } from "../../../i18n/I18nContext";

/** My requests: number, service, status and dates; tap for details and history. */
export default function MyRequestsScreen() {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh } = useLoad(() => api.listMyRequests(), []);

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Title>{t("myRequests")}</Title>
      <Text muted>{t("myRequestsIntro")}</Text>
      {load.status === "loading" && <Loading />}
      {load.status === "error" && (
        <>
          <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
          <Button kind="secondary" label={t("retry")} onPress={refresh} />
        </>
      )}
      {load.status === "ready" && load.data.length === 0 && <Empty text={t("noRequests")} />}
      {load.status === "ready" &&
        load.data.map((r) => (
          <Pressable
            key={r.id}
            accessibilityRole="button"
            accessibilityLabel={`${r.requestNumber}, ${pick(r, "serviceName")}, ${t(`status.${r.status}`)}`}
            onPress={() => router.push(`/requests/${r.id}`)}
          >
            <Card>
              <Row style={{ justifyContent: "space-between" }}>
                <Text bold>{r.requestNumber}</Text>
                <StatusBadge status={r.status} />
              </Row>
              <Text>{pick(r, "serviceName")}</Text>
              <Text muted small>
                {t("createdAt")}: {formatDate(r.createdAt)}
                {r.submittedAt ? ` · ${t("submittedAt")}: ${formatDate(r.submittedAt)}` : ""}
              </Text>
            </Card>
          </Pressable>
        ))}
    </Screen>
  );
}
