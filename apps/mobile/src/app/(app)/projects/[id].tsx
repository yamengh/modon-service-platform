import { router, useLocalSearchParams } from "expo-router";
import { useState } from "react";
import { View } from "react-native";
import { formatDate } from "@portal/i18n/format";
import { api, ApiError, files } from "../../../api/client";
import type { FieldErrors, OfficeDecision, PartnerProjectDetail, PhasePlan, PickedFile } from "../../../api/types";
import { checkPicked, pickFile } from "../../../components/AttachmentField";
import { PHASES, ProgressBars, QuotationSummary, ReportList, officeName, useAmount } from "../../../components/building";
import { useFailureText } from "../../../components/errors";
import { useLoad } from "../../../components/useLoad";
import { Banner, Button, Card, Checkbox, Choice, Detail, Field, Loading, Screen, StatusBadge, Text, Title } from "../../../components/ui";
import { useErrorText, useI18n, type MessageKey } from "../../../i18n/I18nContext";

const OFFICE_FILE = {
  code: "officeAttachment", labelAr: "مرفق عام", labelEn: "General attachment", requiredOnSubmit: false, maxSizeKb: 10240,
  extensions: [".jpeg", ".jpg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"],
};

const thisMonth = () => new Date().toISOString().slice(0, 7);

/** A building project as a consulting office / contractor sees it; what it may do is decided by the backend. */
export default function ProjectScreen() {
  const id = Number(useLocalSearchParams<{ id: string }>().id);
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const amount = useAmount();
  const { load, refreshing, refresh, setData } = useLoad(() => api.getProject(id), [id]);
  const [notice, setNotice] = useState<string | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);

  if (load.status === "loading") return <Loading />;
  if (load.status === "error")
    return (
      <Screen>
        <Banner kind="error">{load.error instanceof ApiError && load.error.status === 404 ? t("projectNotFound") : failure(load.error, "loadFailed")}</Banner>
      </Screen>
    );

  const p = load.data;
  const open = (path: string, name: string) => void api.openProtectedFile(path, name).catch((e) => setFileError(failure(e)));
  const saved = (project: PartnerProjectDetail, key: MessageKey) => {
    setData(project);
    setNotice(t(key));
  };

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      {notice ? <Banner kind="success">{notice}</Banner> : null}
      {fileError ? <Banner kind="error">{fileError}</Banner> : null}
      <Title>{t("buildingTitle")}</Title>
      <StatusBadge status={p.status} />
      <Text muted small>
        {t("requestNumber")}: {p.requestNumber}
        {p.currentRound?.number ? ` · ${t("projectNumber")}: ${p.currentRound.number}` : ""} · {t(`relation.${p.relation}`)}
      </Text>

      {p.canQuote && <QuoteForm project={p} onSaved={(x) => saved(x, "quoteSubmitted")} />}
      {p.canRespond && <RespondForm project={p} onSaved={(x) => saved(x, "answerSent")} />}
      {p.canReport && <ReportForm project={p} onSaved={(x) => saved(x, "reportSubmitted")} />}

      <Card>
        <Title level={2}>{t("projectDetails")}</Title>
        <Detail label={t("investorEstablishment")} value={officeName(pick, p.investor)} />
        <Detail label={t("field.b.licenceTypeCode")} value={p.licenceTypeCode ? t(`licenceType.${p.licenceTypeCode}` as MessageKey) : "—"} />
        <Detail label={t("field.b.constructionBudgetSar")} value={amount(p.constructionBudgetSar)} />
        <Detail label={t("field.b.floorCount")} value={p.floorCount ?? "—"} />
        <Detail label={t("submittedAt")} value={formatDate(p.submittedAt)} />
        <Detail label={t("field.b.scopeOfWork")} value={p.scopeOfWork ?? "—"} />
        <Detail label={t("field.b.notes")} value={p.notes ?? "—"} />
        {p.attachmentSlots.map((slot) => {
          const file = p.attachments.find((a) => a.slotCode === slot.code);
          return file ? <Button key={slot.code} kind="secondary" label={`${pick(slot, "label")}: ${file.fileName}`} onPress={() => open(files.projectAttachment(p.requestId, slot.code), file.fileName)} /> : null;
        })}
      </Card>
      {p.myQuotations.length > 0 && (
        <Card>
          <Title level={2}>{t("myQuotations")}</Title>
          {p.myQuotations.map((q) => (
            <QuotationSummary key={q.id} quotation={q} onOpen={(x) => open(files.project(p.requestId, "quotations", x.id), x.attachment!.fileName)} />
          ))}
        </Card>
      )}
      {p.consultantPlan && (
        <Card>
          <Title level={2}>{t("selectedConsultant")}</Title>
          <QuotationSummary quotation={p.consultantPlan} onOpen={(x) => open(files.project(p.requestId, "quotations", x.id), x.attachment!.fileName)} />
        </Card>
      )}
      {p.relation === "awarded" && (
        <Card>
          <Title level={2}>{t("constructionProgress")}</Title>
          <ProgressBars progress={p.currentProgress} />
          <Title level={3}>{t("progressReports")}</Title>
          <ReportList reports={p.progressReports} onOpen={(r) => open(files.project(p.requestId, "progress-reports", r.id), r.attachment!.fileName)} />
        </Card>
      )}
    </Screen>
  );
}

/** Field errors (400), workflow unavailable (503), already quoted / month taken (409), changed meanwhile (409/404). */
function useSubmit() {
  const { t } = useI18n();
  const errorText = useErrorText();
  const failure = useFailureText();
  const [errors, setErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function run(action: () => Promise<void>) {
    setError(null);
    setBusy(true);
    try {
      await action();
      setErrors({});
    } catch (e) {
      if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) setErrors(e.errors);
      else if (e instanceof ApiError && e.status === 409 && ["quotationAlreadySubmitted", "reportPeriodTaken"].includes(e.message)) setError(errorText(e.message));
      else if (e instanceof ApiError && e.status === 404) setError(t("stageChanged"));
      else setError(failure(e));
    } finally {
      setBusy(false);
    }
  }
  const err = (field: string) => (errors[field]?.[0] ? errorText(errors[field][0]) : null);
  return { setErrors, error, busy, run, err };
}

function FilePicker({ label, file, onPick, error }: { label: string; file: PickedFile | null; onPick: (f: PickedFile | null) => void; error: string | null }) {
  const { t } = useI18n();
  return (
    <View style={{ gap: 4, marginBottom: 10 }}>
      <Text bold small>{label}</Text>
      <Text muted small>{t("officeAttachmentHint")}</Text>
      {file ? <Text small>✓ {file.name}</Text> : null}
      <Button kind="secondary" label={file ? t("replaceFile") : t("pickFile")} onPress={async () => onPick(await pickFile(OFFICE_FILE.extensions))} />
      {error ? <Text small style={{ color: "#b42318" }}>{error}</Text> : null}
    </View>
  );
}

function QuoteForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t, pick } = useI18n();
  const quoted = new Set(project.myQuotations.filter((q) => q.tenderId === project.currentRound?.id).map((q) => q.office.id));
  const offices = project.myOffices.filter((o) => !quoted.has(o.id));
  const [officeId, setOfficeId] = useState(offices.length === 1 ? String(offices[0].id) : "");
  const [amount, setAmount] = useState("");
  const [comment, setComment] = useState("");
  const { setErrors, error, busy, run, err } = useSubmit();

  function submit() {
    const found: FieldErrors = {};
    if (!officeId) found.establishmentId = ["required"];
    if (!amount.trim()) found.amountSar = ["required"];
    else if (!(Number(amount) > 0)) found.amountSar = ["mustBePositive"];
    if (Object.keys(found).length) return setErrors(found);
    void run(async () => onSaved(await api.submitQuotation(project.requestId, Number(officeId), Number(amount), comment.trim() || null)));
  }

  return (
    <Card>
      <Title level={2}>{t("quoteTitle")}</Title>
      <Choice label={t(`quoteOffice.${project.stage}`)} required value={officeId} onChange={setOfficeId} error={err("establishmentId")}
        options={offices.map((o) => ({ value: String(o.id), label: `${pick(o, "name")} – ${o.crNumber}` }))} />
      <Field label={t("quotation.amount")} required value={amount} onChangeText={setAmount} keyboardType="decimal-pad" hint={t("sar")} error={err("amountSar")} />
      <Field label={t("quotation.comment")} value={comment} onChangeText={setComment} multiline error={err("comment")} />
      {error ? <Banner kind="error">{error}</Banner> : null}
      <Button label={t("submitQuote")} busy={busy} onPress={submit} />
    </Card>
  );
}

function RespondForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t } = useI18n();
  const errorText = useErrorText();
  const consultant = project.stage === "consultant";
  const [decision, setDecision] = useState<OfficeDecision | "">("");
  const [amount, setAmount] = useState("");
  const [comment, setComment] = useState("");
  const [commitments, setCommitments] = useState(false);
  const [phases, setPhases] = useState<Record<string, { share: string; days: string }>>(Object.fromEntries(PHASES.map((p) => [p, { share: "", days: "" }])));
  const [file, setFile] = useState<PickedFile | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const { setErrors, error, busy, run, err } = useSubmit();

  function submit() {
    const found: FieldErrors = {};
    if (!decision) found.decision = ["required"];
    if (decision === "reject" && !comment.trim()) found.comment = ["required"];
    if (decision === "accept") {
      if (!amount.trim()) found.amountSar = ["required"];
      else if (!(Number(amount) > 0)) found.amountSar = ["mustBePositive"];
      if (consultant) {
        if (!commitments) found.commitmentsAccepted = ["required"];
        PHASES.forEach((p, i) => {
          const share = phases[p].share.trim();
          const days = phases[p].days.trim();
          if (!share) found[`phases[${i}].sharePercent`] = ["required"];
          else if (!(Number(share) >= 0 && Number(share) <= 100)) found[`phases[${i}].sharePercent`] = ["outOfRange"];
          if (!days) found[`phases[${i}].durationDays`] = ["required"];
          else if (!Number.isInteger(Number(days)) || Number(days) < 1) found[`phases[${i}].durationDays`] = ["mustBePositive"];
        });
      }
    }
    const problem = file ? checkPicked(file, OFFICE_FILE) : null;
    setFileError(problem ? errorText(problem) : null);
    if (Object.keys(found).length || problem) return setErrors(found);

    const plan: PhasePlan[] | null =
      decision === "accept" && consultant ? PHASES.map((p) => ({ phase: p, sharePercent: Number(phases[p].share), durationDays: Number(phases[p].days) })) : null;
    void run(async () => {
      const result = await api.respondToInvitation(
        project.requestId,
        { decision: decision as OfficeDecision, amountSar: decision === "accept" ? Number(amount) : null, comment: comment.trim() || null, commitmentsAccepted: decision === "accept" && commitments, phases: plan },
        decision === "accept" ? file : null,
      );
      if (result) onSaved(result);
      else router.replace("/projects");
    });
  }

  return (
    <Card>
      <Title level={2}>{t("quoteTitle")}</Title>
      <Choice label={t("quoteTitle")} required value={decision} onChange={(d) => setDecision(d)} error={err("decision")}
        options={[{ value: "accept", label: t("decision.accept") }, { value: "reject", label: t("decision.reject") }]} />
      {decision === "accept" && consultant && (
        <>
          <Text bold>{t("phasePlan")}</Text>
          {PHASES.map((p, i) => (
            <View key={p} style={{ gap: 2 }}>
              <Text small bold>{t(`phase.${p}`)}</Text>
              <Field label={`${t(`phase.${p}`)} – ${t("phase.share")}`} value={phases[p].share} keyboardType="decimal-pad"
                onChangeText={(v) => setPhases((x) => ({ ...x, [p]: { ...x[p], share: v } }))} error={err(`phases[${i}].sharePercent`)} />
              <Field label={`${t(`phase.${p}`)} – ${t("phase.duration")}`} value={phases[p].days} keyboardType="number-pad"
                onChangeText={(v) => setPhases((x) => ({ ...x, [p]: { ...x[p], days: v } }))} error={err(`phases[${i}].durationDays`)} />
            </View>
          ))}
          {err("phases") ? <Text small style={{ color: "#b42318" }}>{err("phases")}</Text> : null}
          <Checkbox label={`* ${t("commitmentsAccepted")}`} checked={commitments} onChange={setCommitments} error={err("commitmentsAccepted")} />
        </>
      )}
      {decision === "accept" && (
        <>
          <Field label={t("quotation.amount")} required value={amount} onChangeText={setAmount} keyboardType="decimal-pad" hint={t("sar")} error={err("amountSar")} />
          <FilePicker label={t(`officeAttachment.${project.stage}`)} file={file} onPick={setFile} error={fileError ?? err("file")} />
        </>
      )}
      {decision ? <Field label={t("quotation.comment")} required={decision === "reject"} value={comment} onChangeText={setComment} multiline error={err("comment")} /> : null}
      {error ? <Banner kind="error">{error}</Banner> : null}
      <Button label={t("submitQuote")} busy={busy} onPress={submit} />
    </Card>
  );
}

function ReportForm({ project, onSaved }: { project: PartnerProjectDetail; onSaved: (p: PartnerProjectDetail) => void }) {
  const { t } = useI18n();
  const errorText = useErrorText();
  const [period, setPeriod] = useState(thisMonth());
  const [values, setValues] = useState<Record<string, string>>(Object.fromEntries(project.currentProgress.map((p) => [p.phase, String(p.completionPercent ?? 0)])));
  const [comment, setComment] = useState("");
  const [file, setFile] = useState<PickedFile | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const { setErrors, error, busy, run, err } = useSubmit();

  function submit() {
    const found: FieldErrors = {};
    if (!/^\d{4}-\d{2}$/.test(period.trim())) found.period = ["invalidFormat"];
    PHASES.forEach((p, i) => {
      const v = (values[p] ?? "").trim();
      if (!v) found[`phases[${i}].completionPercent`] = ["required"];
      else if (!(Number(v) >= 0 && Number(v) <= 100)) found[`phases[${i}].completionPercent`] = ["outOfRange"];
    });
    const problem = file ? checkPicked(file, OFFICE_FILE) : null;
    setFileError(problem ? errorText(problem) : null);
    if (Object.keys(found).length || problem) return setErrors(found);
    void run(async () =>
      onSaved(await api.submitProgressReport(project.requestId, { period: period.trim(), phases: PHASES.map((p) => ({ phase: p, completionPercent: Number(values[p]) })), comment: comment.trim() || null }, file)),
    );
  }

  return (
    <Card>
      <Title level={2}>{t("newReport")}</Title>
      <Text muted small>{t("reportHint")}</Text>
      <Field label={t("report.period")} required value={period} onChangeText={setPeriod} hint={t("reportMonthHint")} keyboardType="numbers-and-punctuation" error={err("period")} />
      {PHASES.map((p, i) => (
        <Field key={p} label={`${t(`phase.${p}`)} – ${t("report.completion")}`} value={values[p] ?? ""} keyboardType="decimal-pad"
          onChangeText={(v) => setValues((x) => ({ ...x, [p]: v }))} error={err(`phases[${i}].completionPercent`)} />
      ))}
      {err("phases") ? <Text small style={{ color: "#b42318" }}>{err("phases")}</Text> : null}
      <Field label={t("quotation.comment")} value={comment} onChangeText={setComment} multiline error={err("comment")} />
      <FilePicker label={t("attachment")} file={file} onPick={setFile} error={fileError ?? err("file")} />
      {error ? <Banner kind="error">{error}</Banner> : null}
      <Button label={t("submitReport")} busy={busy} onPress={submit} />
    </Card>
  );
}
