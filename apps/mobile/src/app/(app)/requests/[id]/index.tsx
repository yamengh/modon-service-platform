import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useState } from "react";
import { View } from "react-native";
import { formatDate, formatDateTime, formatPlainDate } from "@portal/i18n/format";
import { api, files } from "../../../../api/client";
import type { AttachmentSlot, BuildingStage, ExcavationData, LookupCategory, LookupOption, RequestDetail } from "../../../../api/types";
import {
  ChooseOfficePanel, PHASES, ProgressBars, QuotationSummary, QuotationsPanel, ReportList, officeName, useAmount, useWaitingText,
} from "../../../../components/building";
import { useFailureText } from "../../../../components/errors";
import { useLoad } from "../../../../components/useLoad";
import { Banner, Button, Card, Detail, Loading, Row, Screen, StatusBadge, Text, Title, colors } from "../../../../components/ui";
import { useI18n, type MessageKey } from "../../../../i18n/I18nContext";
import { FIELD_CATEGORY } from "../../../../requests/excavation";
import { actionRoute, formRoute } from "../../../../requests/routes";


/** A request with its current task, details, documents and the complete status history (with reviewer comments). */
export default function RequestDetailScreen() {
  const id = Number(useLocalSearchParams<{ id: string }>().id);
  const { t, pick } = useI18n();
  const failure = useFailureText();
  const { load, refreshing, refresh, setData } = useLoad(() => api.getRequest(id), [id]);
  const [notice, setNotice] = useState<string | null>(null);
  const [lookups, setLookups] = useState<Record<string, LookupOption[]>>({});
  const [fileError, setFileError] = useState<string | null>(null);

  const isExcavation = load.status === "ready" && load.data.serviceCode === "excavation-permit";
  useEffect(() => {
    if (!isExcavation) return;
    const categories = [...new Set(Object.values(FIELD_CATEGORY))] as LookupCategory[];
    Promise.all(categories.map((c) => api.listLookups(c).then((o) => [c, o] as const)))
      .then((pairs) => setLookups(Object.fromEntries(pairs)))
      .catch(() => undefined); // names fall back to codes
  }, [isExcavation]);

  if (load.status === "loading") return <Loading />;
  if (load.status === "error")
    return (
      <Screen>
        <Banner kind="error">{failure(load.error, "loadFailed")}</Banner>
        <Button kind="secondary" label={t("retry")} onPress={refresh} />
      </Screen>
    );

  const r = load.data;
  const next = actionRoute(r.serviceCode, r.id, r.status);
  const open = (path: string, name: string) => api.openProtectedFile(path, name).catch((e) => setFileError(failure(e)));

  const docs = (slots: AttachmentSlot[]) =>
    slots.map((slot) => {
      const file = r.attachments.find((a) => a.slotCode === slot.code);
      return (
        <View key={slot.code} style={{ gap: 4, marginBottom: 6 }}>
          <Text small bold>{pick(slot, "label")}</Text>
          {file ? <Button kind="secondary" label={`${t("openFile")}: ${file.fileName}`} onPress={() => void open(files.attachment(r.id, slot.code), file.fileName)} /> : <Text muted small>—</Text>}
        </View>
      );
    });

  return (
    <Screen refreshing={refreshing} onRefresh={refresh}>
      {notice ? <Banner kind="info">{notice}</Banner> : null}
      <Row style={{ justifyContent: "space-between" }}>
        <Title>{pick(r, "serviceName")}</Title>
        <StatusBadge status={r.status} />
      </Row>
      <Text muted small>
        {t("requestNumber")}: {r.requestNumber} · {t("createdAt")}: {formatDate(r.createdAt)}
        {r.submittedAt ? ` · ${t("submittedAt")}: ${formatDate(r.submittedAt)}` : ""}
      </Text>

      {r.currentTask ? (
        <Card accent="#b26a00">
          <Text bold>
            {r.currentTask.kind === "completeReturnedRequest" ? t("returnedTitle") : r.currentTask.kind === "uploadCoordinationLetters" ? t("lettersTitle") : t(`task.${r.currentTask.kind}`)}
          </Text>
          <Text small>{t("taskDate")}: {formatDateTime(r.currentTask.assignedAt)}</Text>
          <Text>{t("instructions")}: {r.currentTask.instructions ?? t("noInstructions")}</Text>
          {next ? <Button label={t(`task.${r.currentTask.kind}`)} onPress={() => router.push(next)} /> : null}
        </Card>
      ) : r.canEdit ? (
        <Button label={t("continueEditing")} onPress={() => router.push(formRoute(r.serviceCode, r.id))} />
      ) : null}
      {fileError ? <Banner kind="error">{fileError}</Banner> : null}

      {r.building ? (
        <BuildingPart request={r} onUpdated={setData} onConflict={() => { setNotice(t("stageChanged")); refresh(); }} open={open} />
      ) : (
        <ExcavationPart request={r} lookups={lookups} />
      )}

      <Card>
        <Title level={2}>{t("sectionAttachments")}</Title>
        {docs(r.attachmentSlots)}
      </Card>
      {r.coordinationLetterSlots.some((s) => r.attachments.some((a) => a.slotCode === s.code)) && (
        <Card>
          <Title level={2}>{t("coordinationLetters")}</Title>
          {docs(r.coordinationLetterSlots)}
        </Card>
      )}

      <Card>
        <Title level={2}>{t("statusHistory")}</Title>
        {r.history.map((h, i) => (
          <View key={i} style={{ borderColor: colors.green, borderStartWidth: 3, paddingHorizontal: 8, marginBottom: 8, gap: 2 }}>
            <Text bold>{t(`action.${h.action}`)}</Text>
            <StatusBadge status={h.toStatus} />
            <Text muted small>{formatDateTime(h.occurredAt)}</Text>
            {h.comment ? <Text>{h.comment}</Text> : null}
          </View>
        ))}
      </Card>
    </Screen>
  );

}

function ExcavationPart({ request, lookups: options }: { request: RequestDetail; lookups: Record<string, LookupOption[]> }) {
  const { t, pick } = useI18n();
  const x = request.excavation;
  const value = (field: keyof ExcavationData): string => {
    const raw = x[field];
    if (raw === null || raw === "") return "—";
    const category = FIELD_CATEGORY[field];
    if (category) {
      const found = options[category]?.find((o) => o.code === raw);
      return found ? pick(found, "name") : String(raw);
    }
    if (field === "issueDate" || field === "endDate") return formatPlainDate(String(raw));
    if (typeof raw === "number") return `${raw} ${t("metres")}`;
    return String(raw);
  };
  const sections: [MessageKey, (keyof ExcavationData)[]][] = [
    ["sectionRequest", ["requestTypeCode", "contractTypeCode", "industrialCityCode", "permitTypeCode", "workDescription"]],
    ["sectionPermit", ["issueDate", "endDate", "excavationMethodCode", "workLocation"]],
    ["sectionDimensions", ["lengthM", "widthM", "depthM", "totalPathLengthM"]],
    ["sectionProjectManager", ["projectManagerName", "projectManagerPhone", "projectManagerEmail", "supervisingEntity"]],
  ];
  return (
    <>
      {sections.map(([title, fields]) => (
        <Card key={title}>
          <Title level={2}>{t(title)}</Title>
          {fields.map((f) => (
            <Detail key={f} label={t(`field.${f}` as MessageKey)} value={value(f)} />
          ))}
        </Card>
      ))}
    </>
  );
}

function BuildingPart({
  request: r, onUpdated, onConflict, open,
}: { request: RequestDetail; onUpdated: (r: RequestDetail) => void; onConflict: () => void; open: (path: string, name: string) => Promise<void> }) {
  const { t, pick } = useI18n();
  const amount = useAmount();
  const waiting = useWaitingText();
  const b = r.building!;
  const f = b.form;
  const can = (a: string) => b.investorActions.includes(a as never);

  const panels = (["consultant", "contractor"] as BuildingStage[]).map((stage) => {
    const prefix = stage === "consultant" ? "Consultant" : "Contractor";
    const round = b.tenders.filter((x) => x.stage === stage && x.state === "open").at(-1);
    if (can(`open${prefix}Tender`) || can(`select${prefix}`))
      return <ChooseOfficePanel key={stage} request={r} stage={stage} onUpdated={onUpdated} onConflict={onConflict} />;
    if (round && can(`accept${prefix}Quotation`))
      return <QuotationsPanel key={stage} request={r} stage={stage} round={round} onUpdated={onUpdated} onConflict={onConflict} />;
    if (round?.mode === "direct") return <Banner key={stage} kind="info">{waiting(round, stage)}</Banner>;
    return null;
  });

  return (
    <>
      {panels}
      {r.status === "underModonReview" && <Banner kind="info">{t("underReviewNote")}</Banner>}
      <Card>
        <Title level={2}>{t("sectionBasicInfo")}</Title>
        <Detail label={t("field.b.contractNumber")} value={f.contractNumber ?? "—"} />
        <Detail label={t("field.b.licenceTypeCode")} value={f.licenceTypeCode ? t(`licenceType.${f.licenceTypeCode}` as MessageKey) : "—"} />
        <Detail label={t("field.b.constructionBudgetSar")} value={amount(f.constructionBudgetSar)} />
        <Detail label={t("field.b.floorCount")} value={f.floorCount ?? "—"} />
        <Detail label={t("field.b.scopeOfWork")} value={f.scopeOfWork ?? "—"} />
        <Detail label={t("field.b.notes")} value={f.notes ?? "—"} />
      </Card>
      <Card>
        <Title level={2}>{t("contacts")}</Title>
        {f.contacts.length === 0 ? <Text muted>{t("noContacts")}</Text> : f.contacts.map((c, i) => <Text key={i}>{c.name} · {c.email} · {c.mobile}</Text>)}
      </Card>
      {b.consultantQuotation && (
        <Card>
          <Title level={2}>{t("selectedConsultant")}</Title>
          <QuotationSummary quotation={b.consultantQuotation} onOpen={(q) => void open(files.building(r.id, "quotations", q.id), q.attachment!.fileName)} />
        </Card>
      )}
      {b.contractorQuotation && (
        <Card>
          <Title level={2}>{t("selectedContractor")}</Title>
          <QuotationSummary quotation={b.contractorQuotation} onOpen={(q) => void open(files.building(r.id, "quotations", q.id), q.attachment!.fileName)} />
        </Card>
      )}
      {(r.status === "approved" || b.progressReports.length > 0) && (
        <Card>
          <Title level={2}>{t("constructionProgress")}</Title>
          <ProgressBars progress={b.currentProgress.length ? b.currentProgress : PHASES.map((phase) => ({ phase, completionPercent: 0 }))} />
          <Title level={3}>{t("progressReports")}</Title>
          <ReportList reports={b.progressReports} onOpen={(rep) => void open(files.building(r.id, "progress-reports", rep.id), rep.attachment!.fileName)} />
        </Card>
      )}
      {b.tenders.length > 0 && (
        <Card>
          <Title level={2}>{t("rounds")}</Title>
          {b.tenders.map((round) => (
            <View key={round.id} style={{ marginBottom: 8, gap: 2 }}>
              <Text bold>
                {t(`round.${round.stage}`)} · {round.mode === "openTender" ? t("mode.openTender") : t(`mode.direct.${round.stage}`)}
                {round.number ? ` · ${round.number}` : ""}
              </Text>
              {round.invitedOffice ? <Text small>{officeName(pick, round.invitedOffice)}</Text> : null}
              <Text muted small>{t(`roundState.${round.state}`)} · {formatDateTime(round.openedAt)}</Text>
              {round.quotations.map((q) => (
                <Text key={q.id} small>
                  • {pick(q.office, "name")}: {amount(q.amountSar)} · {t(`quotationState.${q.state}`)}
                  {q.comment ? ` — ${q.comment}` : ""}
                </Text>
              ))}
            </View>
          ))}
        </Card>
      )}
    </>
  );
}
