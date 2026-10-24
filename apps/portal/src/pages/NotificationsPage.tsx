import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { notificationsApi } from "../api/client";
import type { NotificationPage, NotificationView } from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { formatDateTime } from "../i18n/format";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; page: NotificationPage };

/** The signed-in user's notifications. The backend only ever returns the caller's own. */
export function NotificationsPage() {
  const { t, pick } = useI18n();
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [items, setItems] = useState<NotificationView[]>([]);

  const fetchPage = useCallback(
    async (page: number) => {
      try {
        const result = await notificationsApi.list(page, unreadOnly);
        setItems((current) => (page === 1 ? result.items : [...current, ...result.items]));
        setLoad({ status: "ready", page: result });
      } catch {
        setLoad({ status: "error" });
      }
    },
    [unreadOnly],
  );

  useEffect(() => {
    setLoad({ status: "loading" });
    void fetchPage(1);
  }, [fetchPage]);

  async function markRead(n: NotificationView) {
    if (n.read) return;
    setItems((list) => list.map((x) => (x.id === n.id ? { ...x, read: true } : x)));
    try {
      await notificationsApi.markRead(n.id);
    } catch {
      // the next load shows the real state
    }
  }

  async function markAll() {
    try {
      await notificationsApi.markAllRead();
      await fetchPage(1);
    } catch {
      setLoad({ status: "error" });
    }
  }

  return (
    <main className="page">
      <h1>{t("notifications")}</h1>
      <p className="muted">{t("notificationsIntro")}</p>
      <div className="toolbar">
        <label className="checkbox">
          <input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)} />
          <span>{t("unreadOnly")}</span>
        </label>
        {load.status === "ready" && load.page.unreadCount > 0 && (
          <button type="button" className="btn secondary small" onClick={() => void markAll()}>
            {t("markAllRead")}
          </button>
        )}
      </div>

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <p className="alert" role="alert">
          {t("loadFailed")}
        </p>
      )}
      {load.status === "ready" && items.length === 0 && <p className="status">{t("noNotifications")}</p>}
      {items.length > 0 && (
        <ol className="notification-list">
          {items.map((n) => (
            <li key={n.id} className={`card notification ${n.read ? "" : "unread"}`}>
              <div>
                <p>
                  <strong>{pick(n, "title")}</strong> {!n.read && <span className="badge">{t("unreadLabel")}</span>}
                </p>
                {n.comment && <p className="history-comment">{n.comment}</p>}
                <p className="meta">{formatDateTime(n.createdAt)}</p>
              </div>
              <Link className="btn primary small" to={n.link} onClick={() => void markRead(n)}>
                {t("openRequest")}
              </Link>
            </li>
          ))}
        </ol>
      )}
      {load.status === "ready" && items.length < load.page.total && (
        <button type="button" className="btn secondary" onClick={() => void fetchPage(load.page.page + 1)}>
          {t("loadMore")}
        </button>
      )}
      <p className="hint">{t("notificationDeliveryNote")}</p>
    </main>
  );
}
