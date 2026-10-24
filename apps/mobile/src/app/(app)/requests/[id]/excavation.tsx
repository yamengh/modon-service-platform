import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useState } from "react";
import { fill } from "@portal/i18n/format";
import {
  DATE_FIELDS, DIMENSION_FIELDS, REQUIRED_FIELDS, toData, toForm, validateExcavation, type ExcavationForm,
} from "@portal/requests/excavationForm";
import { api, ApiError } from "../../../../api/client";
import type { AttachmentInfo, Establishment, ExcavationData, FieldErrors, LookupCategory, LookupOption, RequestDetail } from "../../../../api/types";
import { AttachmentField } from "../../../../components/AttachmentField";
import { useFailureText } from "../../../../components/errors";
import { Banner, Button, Card, Choice, Field, Loading, Screen, Text, Title } from "../../../../components/ui";
import { useErrorText, useI18n, type MessageKey } from "../../../../i18n/I18nContext";
import { FIELD_CATEGORY, isValidDate } from "../../../../requests/excavation";

type Options = { establishments: Establishment[]; lookups: Record<string, LookupOption[]> };

/** The Excavation Works Permit form: draft, submit, or edit and resubmit a returned request. */
export default function ExcavationFormScreen() {
  const id = Number(useLocalSearchParams<{ id: string }>().id);
  const { t } = useI18n();
  const failure = useFailureText();
  const [state, setState] = useState<{ request: RequestDetail; options: Options } | { error: unknown } | null>(null);

  useEffect(() => {
    let active = true;
    const categories = [...new Set(Object.values(FIELD_CATEGORY))] as LookupCategory[];
    Promise.all([
      api.getRequest(id),
      api.myEstablishments(),
      Promise.all(categories.map((c) => api.listLookups(c).then((o) => [c, o] as const))),
    ])
      .then(([request, establishments, pairs]) => active && setState({ request, options: { establishments, lookups: Object.fromEntries(pairs) } }))
      .catch((error) => active && setState({ error }));
    return () => {
      active = false;
    };
  }, [id]);

  if (!state) return <Loading />;
  if ("error" in state) return <Screen><Banner kind="error">{failure(state.error, "loadFailed")}</Banner></Screen>;
  if (!state.request.canEdit)
    return (
      <Screen>
        <Banner kind="info">{t("requestLocked")}</Banner>
        <Button label={t("viewDetails")} onPress={() => router.replace(`/requests/${id}`)} />
      </Screen>
    );
  return <Editor request={state.request} options={state.options} />;
}

function Editor({ request, options }: { request: RequestDetail; options: Options }) {
  const { t, pick } = useI18n();
  const errorText = useErrorText();
  const failure = useFailureText();
  const [form, setForm] = useState<ExcavationForm>(() => toForm(request.excavation));
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const [busy, setBusy] = useState<"save" | "submit" | null>(null);
  const returned = request.status === "returnedForCompletion";
  const locked = request.submittedAt !== null;

  const set = (field: keyof ExcavationData) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }));
    setErrors(({ [field]: _removed, ...rest }) => rest);
  };
  const err = (field: string) => (errors[field]?.[0] ? errorText(errors[field][0]) : null);

  /** The portal's rules, plus a date format check (dates are typed on a phone). */
  const validate = (mode: "draft" | "submit") => {
    const found = validateExcavation(form, mode, request.attachmentSlots, attachments);
    for (const field of DATE_FIELDS) if (form[field].trim() && !isValidDate(form[field])) found[field] = ["invalidDate"];
    return found;
  };
  const show = (found: FieldErrors, failedKey: MessageKey) => {
    setErrors(found);
    const count = Object.keys(found).length;
    setBanner({ kind: "error", text: `${t(failedKey)} ${count ? fill(t("fixErrors"), { count }) : ""}`.trim() });
  };
  const handle = (e: unknown, failedKey: MessageKey) => {
    if (e instanceof ApiError && e.status === 400 && Object.keys(e.errors).length) show(e.errors, failedKey);
    else setBanner({ kind: "error", text: failure(e, failedKey) });
  };

  async function save() {
    const found = validate("draft");
    if (Object.keys(found).length) return show(found, "saveFailed");
    setBusy("save");
    try {
      await api.saveExcavation(request.id, toData(form));
      setErrors({});
      setBanner({ kind: "success", text: t(returned ? "changesSaved" : "draftSaved") });
    } catch (e) {
      handle(e, "saveFailed");
    } finally {
      setBusy(null);
    }
  }

  async function submit() {
    const failedKey = returned ? "resubmitFailed" : "submitFailed";
    const found = validate("submit");
    if (Object.keys(found).length) return show(found, failedKey);
    setBusy("submit");
    try {
      await api.saveExcavation(request.id, toData(form));
      await api.submitRequest(request.id);
      router.replace(`/requests/${request.id}`);
    } catch (e) {
      handle(e, failedKey);
      setBusy(null);
    }
  }

  const label = (field: keyof ExcavationData) => t(`field.${field}` as MessageKey);
  const required = (field: keyof ExcavationData) => REQUIRED_FIELDS.includes(field);
  const select = (field: keyof typeof FIELD_CATEGORY) => (
    <Choice
      label={label(field)}
      required
      value={form[field]}
      onChange={set(field)}
      error={err(field)}
      options={(options.lookups[FIELD_CATEGORY[field]!] ?? []).map((o) => ({ value: o.code, label: pick(o, "name") }))}
    />
  );
  const input = (field: keyof ExcavationData, kind: "text" | "date" | "number" | "email" | "phone" | "long" = "text") => (
    <Field
      label={label(field)}
      required={required(field)}
      value={form[field]}
      onChangeText={set(field)}
      error={err(field)}
      hint={kind === "date" ? t("dateHint") : undefined}
      multiline={kind === "long"}
      keyboardType={kind === "number" ? "decimal-pad" : kind === "email" ? "email-address" : kind === "phone" ? "phone-pad" : kind === "date" ? "numbers-and-punctuation" : "default"}
      autoCapitalize={kind === "email" ? "none" : undefined}
    />
  );

  return (
    <Screen>
      <Title>{t("excavationTitle")}</Title>
      <Text muted small>{t("requestNumber")}: {request.requestNumber}</Text>
      {request.currentTask ? (
        <Banner kind="info">
          {t("returnedTitle")} — {t("instructions")}: {request.currentTask.instructions ?? t("noInstructions")}
        </Banner>
      ) : null}
      <Text muted small>{t("requiredHint")}</Text>
      {banner ? <Banner kind={banner.kind}>{banner.text}</Banner> : null}

      <Card>
        <Title level={2}>{t("sectionRequest")}</Title>
        {options.establishments.length === 0 ? <Text small>{t("noEstablishments")}</Text> : null}
        <Choice
          label={label("establishmentId")}
          required
          value={form.establishmentId}
          onChange={locked ? () => undefined : set("establishmentId")}
          error={err("establishmentId")}
          options={options.establishments.map((e) => ({ value: String(e.id), label: `${pick(e, "name")} – ${e.crNumber}` }))}
        />
        {locked ? <Text muted small>{t("establishmentLocked")}</Text> : null}
        {select("requestTypeCode")}
        {select("contractTypeCode")}
        {select("industrialCityCode")}
        {select("permitTypeCode")}
        {input("workDescription", "long")}
      </Card>
      <Card>
        <Title level={2}>{t("sectionPermit")}</Title>
        {input("issueDate", "date")}
        {input("endDate", "date")}
        {select("excavationMethodCode")}
        {input("workLocation")}
      </Card>
      <Card>
        <Title level={2}>{t("sectionDimensions")}</Title>
        {DIMENSION_FIELDS.map((f) => <Field key={f} label={label(f)} value={form[f]} onChangeText={set(f)} error={err(f)} keyboardType="decimal-pad" />)}
      </Card>
      <Card>
        <Title level={2}>{t("sectionProjectManager")}</Title>
        {input("projectManagerName")}
        {input("projectManagerPhone", "phone")}
        {input("projectManagerEmail", "email")}
        {input("supervisingEntity")}
      </Card>
      <Title level={2}>{t("sectionAttachments")}</Title>
      {request.attachmentSlots.map((slot) => (
        <AttachmentField
          key={slot.code}
          requestId={request.id}
          slot={slot}
          attachment={attachments.find((a) => a.slotCode === slot.code)}
          error={errors[`attachments.${slot.code}`]?.[0]}
          onUploaded={(info) => {
            setAttachments((list) => [...list.filter((a) => a.slotCode !== info.slotCode), info]);
            setErrors(({ [`attachments.${slot.code}`]: _removed, ...rest }) => rest);
          }}
          onRemoved={(code) => setAttachments((list) => list.filter((a) => a.slotCode !== code))}
        />
      ))}
      <Button label={busy === "submit" ? t(returned ? "resubmitting" : "submitting") : t(returned ? "resubmitRequest" : "submitRequest")} busy={busy === "submit"} disabled={busy !== null} onPress={() => void submit()} />
      <Button kind="secondary" label={busy === "save" ? t("savingDraft") : t(returned ? "saveChanges" : "saveDraft")} busy={busy === "save"} disabled={busy !== null} onPress={() => void save()} />
    </Screen>
  );
}
