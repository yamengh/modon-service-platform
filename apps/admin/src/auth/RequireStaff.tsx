import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useI18n } from "../i18n/I18nContext";
import { useAuth } from "./AuthContext";

/**
 * Guards admin pages: anonymous users go to /login; signed-in users without review access
 * (investors, contractors, consultants) see an explanation. This is only a UX guard —
 * every /api/modon/review endpoint checks the reviewer role on the server.
 */
export function RequireStaff({ children }: { children: ReactNode }) {
  const { state, logout } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  if (state.status === "loading") return <p className="status page">{t("loading")}</p>;

  if (state.status === "anonymous") {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  if (!state.user.canReview) {
    return (
      <main className="page narrow">
        <div className="card" role="alert">
          <h1>{t("noAccessTitle")}</h1>
          <p>{t("noAccessBody")}</p>
          <button type="button" className="btn secondary" onClick={() => void logout()}>
            {t("signOut")}
          </button>
        </div>
      </main>
    );
  }

  return <>{children}</>;
}
