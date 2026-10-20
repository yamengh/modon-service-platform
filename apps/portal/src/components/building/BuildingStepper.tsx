import type { RequestStatus } from "../../api/types";
import { useI18n } from "../../i18n/I18nContext";
import type { MessageKey } from "../../i18n/messages";
import { stageIndex } from "./BuildingParts";

const STEPS: MessageKey[] = ["stage.basicInfo", "stage.consultant", "stage.contractor", "stage.review", "stage.construction"];

/** The building licence's stages, as in the manual's side menu, with the current one marked. */
export function BuildingStepper({ status }: { status: RequestStatus }) {
  const { t } = useI18n();
  const current = stageIndex(status);
  return (
    <ol className="stepper" aria-label={t("stages")}>
      {STEPS.map((key, i) => (
        <li key={key} className={i < current ? "done" : i === current ? "current" : undefined} aria-current={i === current ? "step" : undefined}>
          <span className="step-no">{i + 1}</span>
          {t(key)}
        </li>
      ))}
    </ol>
  );
}
