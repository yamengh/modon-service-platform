import { router } from "expo-router";
import { formatDate } from "@portal/i18n/format";
import { api } from "../../../api/client";
import type { PartnerRelation } from "../../../api/types";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Empty, Loading, Row, Screen, StatusBadge, Text, Title } from "../../../components/ui";
import { useI18n, type MessageKey } from "../../../i18n/I18nContext";

const GROUPS: [PartnerRelation, MessageKey][] = [
  ["invited", "projectsInvited"],
  ["openTender", "projectsOpen"],
  ["awarded", "projectsAwarded"],
];

/** Consulting offices and contractors: open projects, direct invitations, awarded projects. */
export default function ProjectsScreen() {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh } = useLoad(() => api.listProjects(), []);

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      <Title>{t("projects")}</Title>
      <Text muted>{t("projectsIntro")}</Text>
      {load.status === "loading" && <Loading />}
      {load.status === "error" && (
        <>
          <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
          <Button kind="secondary" label={t("retry")} onPress={refresh} />
        </>
      )}
      {load.status === "ready" &&
        GROUPS.map(([relation, title]) => {
          const rows = load.data.filter((p) => p.relation === relation);
          return [
            <Title key={`${relation}-title`} level={2}>
              {t(title)}
            </Title>,
            rows.length === 0 ? (
              <Empty key={`${relation}-empty`} text={t("noProjects")} />
            ) : (
              rows.map((p) => (
                <Card key={`${relation}-${p.requestId}`}>
                  <Row style={{ justifyContent: "space-between" }}>
                    <Text bold>{p.projectNumber ?? p.requestNumber}</Text>
                    <StatusBadge status={p.status} />
                  </Row>
                  <Text small>
                    {t("projectType")}: {p.licenceTypeCode ? t(`licenceType.${p.licenceTypeCode}` as MessageKey) : "—"}
                  </Text>
                  <Text small>
                    {t("facilityName")}: {p.investor ? pick(p.investor, "name") : "—"}
                  </Text>
                  <Text muted small>
                    {t("registrationDate")}: {formatDate(p.since)}
                    {p.answered && relation === "openTender" ? ` · ${t("quoted")}` : ""}
                  </Text>
                  <Button kind="secondary" label={t("open")} onPress={() => router.push(`/projects/${p.requestId}`)} />
                </Card>
              ))
            ),
          ];
        })}
    </Screen>
  );
}
