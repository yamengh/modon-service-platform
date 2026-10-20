import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { TaskItem } from "../api/types";
import { StatusBadge } from "../components/StatusBadge";
import { useI18n } from "../i18n/I18nContext";
import { formatDate } from "../i18n/format";
import { taskPath } from "../requests/serviceAvailability";

type Load = { status: "loading" } | { status: "error" } | { status: "ready"; tasks: TaskItem[] };

/** Requests waiting for the signed-in user. The backend decides which ones; this page only lists them. */
export function TasksPage() {
  const { t, pick } = useI18n();
  const [load, setLoad] = useState<Load>({ status: "loading" });

  useEffect(() => {
    let active = true;
    api
      .listTasks()
      .then((tasks) => active && setLoad({ status: "ready", tasks }))
      .catch(() => active && setLoad({ status: "error" }));
    return () => {
      active = false;
    };
  }, []);

  return (
    <main className="page">
      <h1>{t("tasks")}</h1>
      <p className="muted">{t("tasksIntro")}</p>

      {load.status === "loading" && <p className="status">{t("loading")}</p>}
      {load.status === "error" && (
        <p className="alert" role="alert">
          {t("loadFailed")}
        </p>
      )}
      {load.status === "ready" && load.tasks.length === 0 && <p className="status">{t("noTasks")}</p>}
      {load.status === "ready" && load.tasks.length > 0 && (
        <div className="table-wrap card">
          <table className="requests tasks">
            <thead>
              <tr>
                <th>{t("requestNumber")}</th>
                <th>{t("service")}</th>
                <th>{t("status")}</th>
                <th>{t("instructions")}</th>
                <th>{t("taskDate")}</th>
                <th>{t("task")}</th>
              </tr>
            </thead>
            <tbody>
              {load.tasks.map((task) => {
                const path = taskPath(task);
                return (
                  <tr key={task.requestId}>
                    <td>
                      {/* Offices cannot open the investor's request page; they see the project instead. */}
                      <Link to={task.kind === "respondAsConsultant" || task.kind === "respondAsContractor" ? path : `/requests/${task.requestId}`}>
                        <bdi>{task.requestNumber}</bdi>
                      </Link>
                    </td>
                    <td>{pick(task, "serviceName")}</td>
                    <td>
                      <StatusBadge status={task.status} />
                    </td>
                    <td className="instructions">{task.instructions ?? t("noInstructions")}</td>
                    <td>{formatDate(task.assignedAt)}</td>
                    <td>
                      <Link className="btn primary small" to={path}>
                        {t(`task.${task.kind}`)}
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
}
