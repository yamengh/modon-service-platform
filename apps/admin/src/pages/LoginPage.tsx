import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useI18n } from "../i18n/I18nContext";
import type { MessageKey } from "../i18n/messages";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FieldErrors {
  email?: MessageKey;
  password?: MessageKey;
}

export function LoginPage() {
  const { state, login } = useAuth();
  const { t, toggleLang } = useI18n();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? "/";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failure, setFailure] = useState<MessageKey | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (state.status === "authenticated") return <Navigate to={from} replace />;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const next: FieldErrors = {};
    if (!email.trim()) next.email = "emailRequired";
    else if (!EMAIL_PATTERN.test(email.trim())) next.email = "emailInvalid";
    if (!password) next.password = "passwordRequired";
    setErrors(next);
    setFailure(null);
    if (next.email || next.password) return;

    setSubmitting(true);
    try {
      await login(email.trim(), password);
      navigate(from, { replace: true });
    } catch (err) {
      // FormCMS answers wrong credentials with 400; a 5xx or a network error means the backend is down
      // (the Vite proxy answers 500 when nothing listens on the API port), not that the password is wrong.
      const badCredentials = err instanceof ApiError && (err.status === 400 || err.status === 401);
      setFailure(badCredentials ? "loginFailed" : "loginServerError");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="login-shell">
      <div className="login-card card">
        <div className="login-head">
          <div>
            <p className="eyebrow">{t("appName")}</p>
            <h1>{t("signInTitle")}</h1>
          </div>
          <button type="button" className="link-btn" onClick={toggleLang}>
            {t("switchLang")}
          </button>
        </div>
        <p className="muted">{t("signInHint")}</p>

        <form onSubmit={onSubmit} noValidate>
          <label className="field">
            <span>{t("email")}</span>
            <input
              type="email"
              dir="ltr"
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={!!errors.email}
            />
            {errors.email && <small className="error">{t(errors.email)}</small>}
          </label>

          <label className="field">
            <span>{t("password")}</span>
            <input
              type="password"
              dir="ltr"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={!!errors.password}
            />
            {errors.password && <small className="error">{t(errors.password)}</small>}
          </label>

          {failure && (
            <p className="alert" role="alert">
              {t(failure)}
            </p>
          )}

          <button type="submit" className="btn primary block" disabled={submitting}>
            {submitting ? t("signingIn") : t("signIn")}
          </button>
        </form>
      </div>
    </main>
  );
}
