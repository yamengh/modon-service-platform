import { useState } from "react";
import { useAuth, useUser } from "../../../auth/AuthContext";
import { Button, Card, Choice, Detail, Screen, Title } from "../../../components/ui";
import { apiBaseUrl } from "../../../config";
import { useI18n } from "../../../i18n/I18nContext";

export default function MoreScreen() {
  const { t, lang, setLang } = useI18n();
  const user = useUser();
  const { logout } = useAuth();
  const [busy, setBusy] = useState(false);

  return (
    <Screen>
      <Title>{t("account")}</Title>
      <Card>
        <Detail label={t("email")} value={user.email} />
        <Detail label={t("personasLabel")} value={user.personas.map((p) => t(`persona.${p}`)).join("، ")} />
        <Detail label={t("apiAddress")} value={apiBaseUrl() ?? "—"} />
      </Card>
      <Choice
        label={t("language")}
        value={lang}
        options={[
          { value: "ar", label: t("arabic") },
          { value: "en", label: t("english") },
        ]}
        onChange={setLang}
      />
      <Button
        kind="danger"
        label={t("signOut")}
        busy={busy}
        onPress={async () => {
          setBusy(true);
          await logout();
        }}
      />
    </Screen>
  );
}
