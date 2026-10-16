import { Link, NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { useI18n } from "../i18n/I18nContext";

export function Layout() {
  const { state, logout } = useAuth();
  const { t, toggleLang } = useI18n();
  const user = state.status === "authenticated" ? state.user : null;

  return (
    <>
      <header className="topbar">
        <Link to="/" className="brand">
          <span className="brand-name">{t("appName")}</span>
          <span className="brand-sub">{t("appSubtitle")}</span>
        </Link>
        <nav className="topnav">
          <NavLink to="/" end>
            {t("dashboard")}
          </NavLink>
          <NavLink to="/requests">{t("queue")}</NavLink>
        </nav>
        <div className="topbar-end">
          <button type="button" className="link-btn" onClick={toggleLang}>
            {t("switchLang")}
          </button>
          {user && (
            <>
              <span className="user">
                <span className="user-email">{user.email}</span>
                <span className="badge">{t("reviewerBadge")}</span>
              </span>
              <button type="button" className="btn secondary small" onClick={() => void logout()}>
                {t("signOut")}
              </button>
            </>
          )}
        </div>
      </header>
      <Outlet />
    </>
  );
}
