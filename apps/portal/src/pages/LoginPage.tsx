import { useState, type FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
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
  const from = (location.state as { from?: string } | null)?.from ?? "/services";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failed, setFailed] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  if (state.status === "authenticated") return <Navigate to={from} replace />;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const next: FieldErrors = {};
    if (!email.trim()) next.email = "emailRequired";
    else if (!EMAIL_PATTERN.test(email.trim())) next.email = "emailInvalid";
    if (!password) next.password = "passwordRequired";
    setErrors(next);
    setFailed(false);
    if (next.email || next.password) return;

    setSubmitting(true);
    try {
      await login(email.trim(), password);
      navigate(from, { replace: true });
    } catch {
      setFailed(true);
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

          {failed && (
            <p className="alert" role="alert">
              {t("loginFailed")}
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
