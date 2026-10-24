import { router } from "expo-router";
import { formatDate } from "@portal/i18n/format";
import { api } from "../../../api/client";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Empty, Loading, Row, Screen, StatusBadge, Text, Title } from "../../../components/ui";
import { useI18n } from "../../../i18n/I18nContext";
import { taskRoute } from "../../../requests/routes";

/** What is waiting for me (the backend decides): returns, letters, stage choices, office invitations. */
export default function TasksScreen() {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh } = useLoad(() => api.listTasks(), []);

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Title>{t("tasks")}</Title>
      <Text muted>{t("tasksIntro")}</Text>
      {load.status === "loading" && <Loading />}
      {load.status === "error" && (
        <>
          <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
          <Button kind="secondary" label={t("retry")} onPress={refresh} />
        </>
      )}
      {load.status === "ready" && load.data.length === 0 && <Empty text={t("noTasks")} />}
      {load.status === "ready" &&
        load.data.map((task) => (
          <Card key={`${task.kind}-${task.requestId}`} accent="#b26a00">
            <Row style={{ justifyContent: "space-between" }}>
              <Text bold>{task.requestNumber}</Text>
              <StatusBadge status={task.status} />
            </Row>
            <Text>{pick(task, "serviceName")}</Text>
            <Text small>
              {t("instructions")}: {task.instructions ?? t("noInstructions")}
            </Text>
            <Text muted small>
              {t("taskDate")}: {formatDate(task.assignedAt)}
            </Text>
            <Button label={t(`task.${task.kind}`)} onPress={() => router.push(taskRoute(task))} />
          </Card>
        ))}
    </Screen>
  );
}
