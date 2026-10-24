import { useEffect, useState } from "react";
import { View } from "react-native";
import { fill, formatAmount, formatDateTime, formatPeriod } from "@portal/i18n/format";
import { api, ApiError } from "../api/client";
import type {
  BuildingStage, ConstructionPhase, OfficeInfo, PhasePlan, PhaseProgress, ProgressReportView, QuotationView, RequestDetail, SelectionMode, TenderView,
} from "../api/types";
import { useI18n } from "../i18n/I18nContext";
import { useFailureText } from "./errors";
import { Banner, Button, Card, Choice, Detail, Field, Row, Text, Title, colors } from "./ui";

/** The six construction phases, in the manual's order. */
export const PHASES: ConstructionPhase[] = [
  "sitePreparation",
  "foundations",
  "structuralWorks",
  "wallsInsulationFloors",
  "electricalMechanicalArchitectural",
  "siteFinishingAndFence",
];

export function officeName(pick: (o: object, f: string) => string, office: OfficeInfo | null) {
  return office ? `${pick(office, "name")} (${office.crNumber})` : "—";
}

export function useAmount() {
  const { t } = useI18n();
  return (value: number | null) => (value === null ? "—" : `${formatAmount(value)} ${t("sar")}`);
}

export function PhasePlanList({ phases }: { phases: PhasePlan[] }) {
  const { t } = useI18n();
  return (
    <View style={{ gap: 4 }} accessibilityLabel={t("phasePlan")}>
      {phases.map((p) => (
        <Text key={p.phase} small>
          {t(`phase.${p.phase}`)}: {p.sharePercent ?? "—"}% · {p.durationDays ?? "—"} {t("phase.duration")}
        </Text>
      ))}
    </View>
  );
}

/** Completion per phase as accessible progress bars. */
export function ProgressBars({ progress }: { progress: PhaseProgress[] }) {
  const { t, rtl } = useI18n();
  return (
    <View style={{ gap: 8 }}>
      {progress.map((p) => {
        const value = Math.min(100, Math.max(0, p.completionPercent ?? 0));
        return (
          <View key={p.phase} accessible accessibilityRole="progressbar" accessibilityLabel={t(`phase.${p.phase}`)}
            accessibilityValue={{ min: 0, max: 100, now: value }}>
            <Row style={{ justifyContent: "space-between" }}>
              <Text small>{t(`phase.${p.phase}`)}</Text>
              <Text small muted>{value}%</Text>
            </Row>
            <View style={{ height: 8, backgroundColor: "#eef1f4", borderRadius: 4, overflow: "hidden", flexDirection: rtl ? "row-reverse" : "row" }}>
              <View style={{ width: `${value}%`, backgroundColor: colors.green }} />
            </View>
          </View>
        );
      })}
    </View>
  );
}

export function ReportList({ reports, onOpen }: { reports: ProgressReportView[]; onOpen: (r: ProgressReportView) => void }) {
  const { t, pick } = useI18n();
  if (reports.length === 0) return <Text muted>{t("noReports")}</Text>;
  return (
    <View style={{ gap: 8 }}>
      {[...reports].reverse().map((r) => (
        <Card key={r.id} accent={colors.green}>
          <Text bold>
            {t("report.period")}: {formatPeriod(r.period)}
          </Text>
          <Text muted small>
            {t("report.by")}: {officeName(pick, r.office)} {r.submittedByEmail ? `(${r.submittedByEmail})` : ""} · {formatDateTime(r.submittedAt)}
          </Text>
          {r.phases.map((p) => (
            <Text key={p.phase} small>
              {t(`phase.${p.phase}`)}: {p.completionPercent}%
            </Text>
          ))}
          {r.comment ? <Text>{r.comment}</Text> : null}
          {r.attachment ? <Button kind="secondary" label={`${t("openFile")}: ${r.attachment.fileName}`} onPress={() => onOpen(r)} /> : null}
        </Card>
      ))}
    </View>
  );
}

export function QuotationSummary({ quotation, onOpen }: { quotation: QuotationView; onOpen: (q: QuotationView) => void }) {
  const { t, pick } = useI18n();
  const amount = useAmount();
  return (
    <View style={{ gap: 2 }}>
      <Detail label={t("quotation.office")} value={officeName(pick, quotation.office)} />
      <Detail label={t("quotation.amount")} value={amount(quotation.amountSar)} />
      <Detail label={t("quotation.time")} value={formatDateTime(quotation.submittedAt)} />
      <Detail label={t("quotation.state")} value={t(`quotationState.${quotation.state}`)} />
      {quotation.comment ? <Detail label={t("quotation.comment")} value={quotation.comment} /> : null}
      {quotation.commitmentsAccepted ? <Text small>✓ {t("commitmentsConfirmed")}</Text> : null}
      {quotation.phases.length > 0 && (
        <>
          <Text bold small>{t("phasePlan")}</Text>
          <PhasePlanList phases={quotation.phases} />
        </>
      )}
      {quotation.attachment ? <Button kind="secondary" label={`${t("openFile")}: ${quotation.attachment.fileName}`} onPress={() => onOpen(quotation)} /> : null}
    </View>
  );
}

interface StageProps {
  request: RequestDetail;
  stage: BuildingStage;
  onUpdated: (r: RequestDetail) => void;
  onConflict: () => void;
}

/** Manual 1.2: an open project ("المشروع") or a specific office, then "التقدم في الطلب". */
export function ChooseOfficePanel({ request, stage, onUpdated, onConflict }: StageProps) {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const [mode, setMode] = useState<SelectionMode>("openTender");
  const [offices, setOffices] = useState<OfficeInfo[] | null>(null);
  const [officeId, setOfficeId] = useState("");
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (mode !== "direct" || offices) return;
    api.listOffices(stage).then(setOffices).catch(() => setOffices([]));
  }, [mode, offices, stage]);

  async function proceed() {
    setError(null);
    if (mode === "direct" && !officeId) return setFieldError(t("err.required"));
    setBusy(true);
    try {
      onUpdated(await api.chooseOffice(request.id, stage, mode, mode === "direct" ? Number(officeId) : undefined));
    } catch (e) {
      if (e instanceof ApiError && e.status === 409 && e.message !== "workflowNotStarted") onConflict();
      else setError(failure(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card accent={colors.green}>
      <Title level={2}>{t(`chooseTitle.${stage}`)}</Title>
      <Choice
        label={t(`chooseTitle.${stage}`)}
        value={mode}
        onChange={(m) => setMode(m)}
        options={[
          { value: "openTender", label: t("mode.openTender") },
          { value: "direct", label: t(`mode.direct.${stage}`) },
        ]}
      />
      <Text muted small>{t(mode === "openTender" ? "modeHint.openTender" : "modeHint.direct")}</Text>
      {mode === "direct" &&
        (offices === null ? (
          <Text muted>{t("loading")}</Text>
        ) : offices.length === 0 ? (
          <Text muted>{t("noOffices")}</Text>
        ) : (
          <Choice
            label={t(`office.${stage}`)}
            required
            value={officeId}
            error={fieldError}
            onChange={(v) => {
              setOfficeId(v);
              setFieldError(null);
            }}
            options={offices.map((o) => ({ value: String(o.id), label: `${pick(o, "name")} – ${o.crNumber}` }))}
          />
        ))}
      {error ? <Banner kind="error">{error}</Banner> : null}
      <Button label={busy ? t("proceeding") : t("proceed")} onPress={() => void proceed()} busy={busy} />
    </Card>
  );
}

/** Manual 1.5: pick one quotation ("إختيار عرض سعر"), or reject all / cancel the project. */
export function QuotationsPanel({ request, stage, round, onUpdated, onConflict }: StageProps & { round: TenderView }) {
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const amount = useAmount();
  const [chosen, setChosen] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [comment, setComment] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const pending = round.quotations.filter((q) => q.state === "submitted");

  async function run(action: () => Promise<RequestDetail>) {
    setError(null);
    setBusy(true);
    try {
      onUpdated(await action());
    } catch (e) {
      if (e instanceof ApiError && e.status === 409 && e.message !== "workflowNotStarted") onConflict();
      else setError(failure(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card accent={colors.green}>
      <Title level={2}>{t("quotations")}</Title>
      {round.number ? <Text muted small>{t("projectNumber")}: {round.number}</Text> : null}
      {pending.length === 0 ? (
        <Text muted>{t("noQuotationsYet")}</Text>
      ) : (
        <Choice
          label={t("chooseQuotation")}
          value={chosen}
          onChange={setChosen}
          options={pending.map((q) => ({
            value: String(q.id),
            label: `${pick(q.office, "name")} — ${amount(q.amountSar)}${q.comment ? ` — ${q.comment}` : ""} (${formatDateTime(q.submittedAt)})`,
          }))}
        />
      )}
      {error ? <Banner kind="error">{error}</Banner> : null}
      {rejecting ? (
        <>
          <Field label={t("rejectAllComment")} value={comment} onChangeText={setComment} multiline />
          <Button kind="danger" label={t("confirmRejectAll")} busy={busy} onPress={() => void run(() => api.rejectQuotations(request.id, stage, comment.trim()))} />
          <Button kind="secondary" label={t("cancel")} onPress={() => setRejecting(false)} disabled={busy} />
        </>
      ) : (
        <>
          <Button label={t("chooseQuotation")} disabled={!chosen} busy={busy} onPress={() => void run(() => api.acceptQuotation(request.id, stage, Number(chosen)))} />
          <Button kind="secondary" label={t("rejectAll")} onPress={() => setRejecting(true)} disabled={busy} />
        </>
      )}
    </Card>
  );
}

/** "Waiting for <office>" text for a direct invitation. */
export function useWaitingText() {
  const { t, pick } = useI18n();
  return (round: TenderView, stage: BuildingStage) =>
    fill(t("waitingForOffice"), { office: round.invitedOffice ? pick(round.invitedOffice, "name") : t(`office.${stage}`) });
}
