import { router } from "expo-router";
import { formatDateTime } from "@portal/i18n/format";
import { api } from "../../../api/client";
import type { NotificationView } from "../../../api/types";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Empty, Loading, Row, Screen, Text, Title, colors } from "../../../components/ui";
import { useI18n } from "../../../i18n/I18nContext";

/** The same in-app notifications as the web portal (only the signed-in user's own; the backend filters). */
export default function NotificationsScreen() {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh, setData } = useLoad(() => api.listNotifications(1), []);

  async function open(n: NotificationView) {
    if (!n.read && load.status === "ready") {
      setData({ ...load.data, unreadCount: Math.max(0, load.data.unreadCount - 1), items: load.data.items.map((x) => (x.id === n.id ? { ...x, read: true } : x)) });
      api.markNotificationRead(n.id).catch(() => undefined);
    }
    router.push(n.link);
  }

  async function markAll() {
    await api.markAllNotificationsRead().catch(() => undefined);
    refresh();
  }

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Title>{t("notifications")}</Title>
      <Text muted>{t("notificationsIntro")}</Text>
      {load.status === "loading" && <Loading />}
      {load.status === "error" && (
        <>
          <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
          <Button kind="secondary" label={t("retry")} onPress={refresh} />
        </>
      )}
      {load.status === "ready" && load.data.unreadCount > 0 && <Button kind="secondary" label={t("markAllRead")} onPress={() => void markAll()} />}
      {load.status === "ready" && load.data.items.length === 0 && <Empty text={t("noNotifications")} />}
      {load.status === "ready" &&
        load.data.items.map((n) => (
          <Card key={n.id} accent={n.read ? undefined : colors.green}>
            <Row>
              <Text bold style={{ flexShrink: 1 }}>
                {pick(n, "title")}
              </Text>
              {!n.read && <Text small style={{ color: colors.green }}>{t("unreadLabel")}</Text>}
            </Row>
            {n.comment ? <Text>{n.comment}</Text> : null}
            <Text muted small>{formatDateTime(n.createdAt)}</Text>
            <Button kind="secondary" label={t("openRequest")} onPress={() => void open(n)} />
          </Card>
        ))}
      <Text muted small>{t("notificationDeliveryNote")}</Text>
    </Screen>
  );
}
