import { useEffect, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import type { ReviewQueuePage, ReviewStatus } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { useI18n } from "../i18n/I18nContext";
import { fill, formatDate } from "../i18n/format";
import { useReferenceData } from "../reference/ReferenceData";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; page: ReviewQueuePage };

const STATUSES: ReviewStatus[] = ["inProgress", "returnedForCompletion", "approved", "rejected", "cancelled"];

/** Filters live in the URL (?status=&service=&search=&page=), so links and the back button keep them. */
export function QueuePage() {
  const { t, pick } = useI18n();
  const { services, option } = useReferenceData();
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "";
  const service = params.get("service") ?? "";
  const search = params.get("search") ?? "";
  const page = Math.max(1, Number(params.get("page")) || 1);

  const [searchText, setSearchText] = useState(search);
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => setSearchText(search), [search]);

  useEffect(() => {
    let active = true;
    setLoad({ status: "loading" });
    api
      .listRequests({ status, service, search, page })
      .then((result) => active && setLoad({ status: "ready", page: result }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, [status, service, search, page, attempt]);

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    if (!("page" in changes)) next.delete("page");
    setParams(next);
  };

  const onSearch = (e: FormEvent) => {
    e.preventDefault();
    update({ search: searchText.trim() });
  };

  const pages = load.status === "ready" ? Math.max(1, Math.ceil(load.page.total / load.page.pageSize)) : 1;

  return (
    <main className="page">
      <h1>{t("queue")}</h1>
      <p className="muted">{t("queueIntro")}</p>

      <form className="filters card" onSubmit={onSearch} role="search">
        <label className="field">
          <span>{t("filterStatus")}</span>
          <select value={status} onChange={(e) => update({ status: e.target.value })}>
            <option value="">{t("all")}</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`status.${s}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t("filterService")}</span>
          <select value={service} onChange={(e) => update({ service: e.target.value })}>
            <option value="">{t("all")}</option>
            {services.map((s) => (
              <option key={s.code} value={s.code}>
                {pick(s, "name")}
              </option>
            ))}
          </select>
        </label>
        <label className="field grow">
          <span>{t("searchNumber")}</span>
          <input
            type="search"
            dir="ltr"
            value={searchText}
            placeholder={t("searchPlaceholder")}
            onChange={(e) => setSearchText(e.target.value)}
          />
        </label>
        <div className="filter-actions">
          <button type="submit" className="btn primary">
            {t("search")}
          </button>
          {(status || service || search) && (
            <button type="button" className="btn secondary" onClick={() => setParams(new URLSearchParams())}>
              {t("clearFilters")}
            </button>
          )}
        </div>
      </form>

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <div className="alert" role="alert">
          {t("loadFailed")}{" "}
          <button type="button" className="link-btn" onClick={() => setAttempt((a) => a + 1)}>
            {t("retry")}
          </button>
        </div>
      )}
      {load.status === "ready" && load.page.items.length === 0 && <p className="status">{t("noRequests")}</p>}
      {load.status === "ready" && load.page.items.length > 0 && (
        <>
          <div className="table-wrap card">
            <table className="requests">
              <thead>
                <tr>
                  <th>{t("requestNumber")}</th>
                  <th>{t("service")}</th>
                  <th>{t("applicant")}</th>
                  <th>{t("submittedAt")}</th>
                  <th>{t("status")}</th>
                  <th>{t("industrialCity")}</th>
                </tr>
              </thead>
              <tbody>
                {load.page.items.map((r) => {
                  const city = option("industrialCity", r.industrialCityCode);
                  return (
                    <tr key={r.id}>
                      <td>
                        <Link to={`/requests/${r.id}`}>
                          <bdi>{r.requestNumber}</bdi>
                        </Link>
                      </td>
                      <td>{pick(r, "serviceName")}</td>
                      <td>
                        <span className="cell-main">{r.establishment ? pick(r.establishment, "name") : "—"}</span>
                        <span className="cell-sub" dir="ltr">
                          {r.applicant.email}
                        </span>
                      </td>
                      <td>{formatDate(r.submittedAt)}</td>
                      <td>
                        <StatusBadge status={r.status} />
                      </td>
                      <td>{city ? pick(city, "name") : (r.industrialCityCode ?? "—")}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <nav className="pager" aria-label="pagination">
            <button type="button" className="btn secondary small" disabled={page <= 1} onClick={() => update({ page: String(page - 1) })}>
              {t("previous")}
            </button>
            <span className="meta">{fill(t("pageOf"), { page, pages, total: load.page.total })}</span>
            <button type="button" className="btn secondary small" disabled={page >= pages} onClick={() => update({ page: String(page + 1) })}>
              {t("next")}
            </button>
          </nav>
        </>
      )}
    </main>
  );
}
