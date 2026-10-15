import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { ServiceSummary } from "../api/types";
import { useI18n } from "../i18n/I18nContext";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; services: ServiceSummary[] };

export function ServicesPage() {
  const { t, pick } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    api
      .listServices()
      .then((services) => active && setLoad({ status: "ready", services }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, []);

  return (
    <main className="page">
      <p className="breadcrumb">{t("modonServices")}</p>
      <h1>{t("allServices")}</h1>
      <p className="muted">{t("servicesIntro")}</p>

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <p className="alert" role="alert">
          {t("loadFailed")}
        </p>
      )}
      {load.status === "ready" && load.services.length === 0 && <p className="status">{t("noServices")}</p>}
      {load.status === "ready" && load.services.length > 0 && (
        <ul className="service-grid">
          {load.services.map((s) => (
            <li key={s.code} className="card service-card">
              <span className="badge">{t(`beneficiary.${s.beneficiary}`)}</span>
              <h2>{pick(s, "name")}</h2>
              <p>{pick(s, "summary")}</p>
              {pick(s, "duration") && (
                <p className="meta">
                  {t("duration")}: {pick(s, "duration")}
                </p>
              )}
              <Link className="btn primary" to={`/services/${s.code}`}>
                {t("serviceDetails")}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
