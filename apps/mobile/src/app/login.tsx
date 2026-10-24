import { Redirect } from "expo-router";
import { useState } from "react";
import { View } from "react-native";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { Banner, Button, Field, Screen, Text, Title } from "../components/ui";
import { apiBaseUrl } from "../config";
import { useI18n } from "../i18n/I18nContext";

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function LoginScreen() {
  const { state, login } = useAuth();
  const { t, lang, setLang } = useI18n();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (state.status === "signedIn") return <Redirect href="/" />;

  async function submit() {
    const found: typeof errors = {};
    if (!email.trim()) found.email = t("emailRequired");
    else if (!EMAIL.test(email.trim())) found.email = t("emailInvalid");
    if (!password) found.password = t("passwordRequired");
    setErrors(found);
    setFailure(null);
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      await login(email, password);
    } catch (e) {
      if (e instanceof ApiError && e.message === "apiNotConfigured") setFailure(t("apiNotConfigured"));
      else if (e instanceof ApiError && e.offline) setFailure(t("networkError"));
      else if (e instanceof ApiError && e.status === 403) setFailure(t("appRoleNotAllowed"));
      else if (e instanceof ApiError && e.message === "lockedOut") setFailure(t("lockedOut"));
      else if (e instanceof ApiError && e.status === 401) setFailure(t("loginFailed"));
      else setFailure(t("genericError"));
    } finally {
      setBusy(false);
    }
  }

  const reason = state.status === "signedOut" ? state.reason : undefined;
  return (
    <Screen>
      <View style={{ gap: 4, marginTop: 24 }}>
        <Title>{t("appName")}</Title>
        <Text muted>{t("appSubtitle")}</Text>
      </View>
      <Button kind="secondary" label={lang === "ar" ? "English" : "العربية"} onPress={() => setLang(lang === "ar" ? "en" : "ar")} />
      <Title level={2}>{t("signInTitle")}</Title>
      <Text muted>{t("signInHint")}</Text>
      {reason === "expired" ? <Banner kind="info">{t("sessionExpired")}</Banner> : null}
      {reason === "loggedOut" ? <Banner kind="success">{t("loggedOut")}</Banner> : null}
      {!apiBaseUrl() ? <Banner kind="error">{t("apiNotConfigured")}</Banner> : null}
      {failure ? <Banner kind="error">{failure}</Banner> : null}
      <Field
        label={t("email")}
        value={email}
        onChangeText={setEmail}
        autoCapitalize="none"
        autoComplete="email"
        keyboardType="email-address"
        textContentType="username"
        error={errors.email}
      />
      <Field
        label={t("password")}
        value={password}
        onChangeText={setPassword}
        secureTextEntry
        autoComplete="password"
        textContentType="password"
        error={errors.password}
        onSubmitEditing={() => void submit()}
      />
      <Button label={busy ? t("signingIn") : t("signIn")} onPress={() => void submit()} busy={busy} />
    </Screen>
  );
}
