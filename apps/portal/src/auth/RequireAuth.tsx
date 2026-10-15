import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useI18n } from "../i18n/I18nContext";
import { useAuth } from "./AuthContext";

/**
 * Guards portal pages: anonymous users go to /login, and signed-in users without a
 * portal persona (investor, contractor or consultant) see an explanation instead of the page.
 * The backend decides personas, so this is a UX guard, not the security boundary.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { state, logout } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  if (state.status === "loading") return <p className="status">{t("loading")}</p>;

  if (state.status === "anonymous") {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  if (!state.user.canUsePortal) {
    return (
      <main className="page narrow">
        <div className="card" role="alert">
          <h1>{t("noPortalAccessTitle")}</h1>
          <p>{t("noPortalAccessBody")}</p>
          <button type="button" className="btn secondary" onClick={() => void logout()}>
            {t("signOut")}
          </button>
        </div>
      </main>
    );
  }

  return <>{children}</>;
}
