import { Redirect, Stack } from "expo-router";
import { useAuth } from "../../auth/AuthContext";
import { Banner, Button, Loading, Screen, Title } from "../../components/ui";
import { useI18n } from "../../i18n/I18nContext";

/**
 * Everything inside (app) needs a confirmed session. This only decides what to SHOW — every call is still
 * authorised by the backend (roles, ownership, office membership).
 */
export default function SignedInLayout() {
  const { state, retry, logout } = useAuth();
  const { t } = useI18n();

  if (state.status === "restoring") return <Loading />;
  if (state.status === "signedOut") return <Redirect href="/login" />;
  if (state.status === "offline")
    return (
      <Screen>
        <Title>{t("offlineTitle")}</Title>
        <Banner kind="error">{t("offlineBody")}</Banner>
        <Button label={t("retry")} onPress={() => void retry()} />
        <Button kind="secondary" label={t("signOut")} onPress={() => void logout()} />
      </Screen>
    );
  if (!state.user.canUsePortal)
    return (
      <Screen>
        <Title>{t("noPortalAccessTitle")}</Title>
        <Banner kind="info">{t("appRoleNotAllowed")}</Banner>
        <Button kind="secondary" label={t("signOut")} onPress={() => void logout()} />
      </Screen>
    );

  return (
    <Stack>
      <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
      <Stack.Screen name="services/[code]" options={{ title: t("serviceDetails") }} />
      <Stack.Screen name="requests/[id]/index" options={{ title: t("viewDetails") }} />
      <Stack.Screen name="requests/[id]/excavation" options={{ title: t("excavationTitle") }} />
      <Stack.Screen name="requests/[id]/building" options={{ title: t("buildingTitle") }} />
      <Stack.Screen name="requests/[id]/letters" options={{ title: t("coordinationLetters") }} />
      <Stack.Screen name="projects/[id]" options={{ title: t("projectDetails") }} />
    </Stack>
  );
}
