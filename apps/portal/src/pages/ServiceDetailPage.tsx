import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import type { ServiceDetail } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { useI18n } from "../i18n/I18nContext";
import { OPEN_SERVICES } from "../requests/serviceAvailability";

type Load =
  | { status: "loading" }
  | { status: "error" }
  | { status: "missing" }
  | { status: "ready"; service: ServiceDetail };

const lines = (text: string) => text.split("\n").map((l) => l.trim()).filter(Boolean);

export function ServiceDetailPage() {
  const { code = "" } = useParams();
  const { t, pick } = useI18n();
  const { state } = useAuth();
  const navigate = useNavigate();
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [starting, setStarting] = useState(false);
  const [startFailed, setStartFailed] = useState(false);

  useEffect(() => {
    let active = true;
    setLoad({ status: "loading" });
    api
      .getService(code)
      .then((service) => active && setLoad(service ? { status: "ready", service } : { status: "missing" }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, [code]);

  const back = (
    <Link to="/services" className="breadcrumb">
      {t("backToServices")}
    </Link>
  );

  if (load.status === "loading") return <main className="page">{back}<p className="status">{t("loading")}</p></main>;
  if (load.status === "error")
    return (
      <main className="page">
        {back}
        <p className="alert" role="alert">{t("loadFailed")}</p>
      </main>
    );
  if (load.status === "missing")
    return (
      <main className="page">
        {back}
        <p className="status">{t("serviceNotFound")}</p>
      </main>
    );

  const s = load.service;
  const personas: string[] = state.status === "authenticated" ? state.user.personas : [];
  const forOtherPersona = !personas.includes(s.beneficiary);
  // Only services with a portal form can be started, and only by the personas allowed to apply.
  const open = OPEN_SERVICES[s.code];
  const canStart = !!open && open.personas.some((p) => personas.includes(p));

  async function start() {
    setStarting(true);
    setStartFailed(false);
    try {
      const draft = await api.createRequest(s.code);
      navigate(open.formPath(draft.id));
    } catch {
      setStartFailed(true);
      setStarting(false);
    }
  }
  const info: [string, string][] = [
    [t("beneficiary"), t(`beneficiary.${s.beneficiary}`)],
    [t("duration"), pick(s, "duration") || t("notSpecified")],
    [t("channel"), pick(s, "channel") || t("notSpecified")],
    [t("cost"), pick(s, "cost") || t("notSpecified")],
  ];

  return (
    <main className="page">
      {back}
      <div className="detail">
        <section>
          <h1>{pick(s, "name")}</h1>
          <p className="lead">{pick(s, "description") || pick(s, "summary")}</p>

          <h2>{t("steps")}</h2>
          <ol className="steps">
            {lines(pick(s, "steps")).map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>

          <h2>{t("requiredDocuments")}</h2>
          <ul className="docs">
            {lines(pick(s, "requiredDocuments")).map((doc) => (
              <li key={doc}>{doc}</li>
            ))}
          </ul>
        </section>

        <aside className="card info">
          <button
            type="button"
            className="btn primary block"
            disabled={!canStart || starting}
            aria-describedby="start-hint"
            onClick={() => void start()}
          >
            {starting ? t("startingService") : t("startService")}
          </button>
          <div id="start-hint">
            {!open && <p className="hint">{t("startServiceSoon")}</p>}
            {forOtherPersona && <p className="hint warn">{t("serviceForOtherPersona")}</p>}
          </div>
          {startFailed && (
            <p className="alert" role="alert">
              {t("startFailed")}
            </p>
          )}
          <dl>
            {info.map(([label, value]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </aside>
      </div>
    </main>
  );
}
