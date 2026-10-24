import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation } from "react-router-dom";
import { notificationsApi } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useI18n } from "../i18n/I18nContext";

export function Layout() {
  const { state, logout } = useAuth();
  const { t, toggleLang } = useI18n();
  const user = state.status === "authenticated" ? state.user : null;
  const location = useLocation();
  const [unread, setUnread] = useState(0);

  // Refreshed on every page change; a failure just hides the badge.
  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const count = await notificationsApi.unreadCount();
        if (active) setUnread(typeof count === "number" ? count : 0);
      } catch {
        if (active) setUnread(0);
      }
    })();
    return () => {
      active = false;
    };
  }, [location.pathname]);

  return (
    <>
      <header className="topbar">
        <Link to="/services" className="brand">
          <span className="brand-name">{t("appName")}</span>
          <span className="brand-sub">{t("appSubtitle")}</span>
        </Link>
        <nav className="topnav">
          <NavLink to="/services">{t("modonServices")}</NavLink>
          <NavLink to="/requests">{t("myRequests")}</NavLink>
          <NavLink to="/tasks">{t("tasks")}</NavLink>
          {user?.personas.some((p) => p === "consultant" || p === "contractor") && <NavLink to="/projects">{t("projects")}</NavLink>}
          <NavLink to="/notifications" aria-label={unread > 0 ? `${t("notifications")} (${unread})` : t("notifications")}>
            {t("notifications")}
            {unread > 0 && <span className="count-badge">{unread}</span>}
          </NavLink>
        </nav>
        <div className="topbar-end">
          <button type="button" className="link-btn" onClick={toggleLang}>
            {t("switchLang")}
          </button>
          {user && (
            <>
              <span className="user">
                <span className="user-email">{user.email}</span>
                {user.personas.map((p) => (
                  <span key={p} className="badge">
                    {t(`persona.${p}`)}
                  </span>
                ))}
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
