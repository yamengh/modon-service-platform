import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useState } from "react";
import { View } from "react-native";
import { fill } from "@portal/i18n/format";
import {
  LICENCE_TYPES, MAX_CONTACTS, REQUIRED_BUILDING_FIELDS, toBuildingData, toBuildingForm, validateBuilding, type BuildingForm, type ContactForm,
} from "@portal/requests/buildingForm";
import { api, ApiError } from "../../../../api/client";
import type { AttachmentInfo, Establishment, FieldErrors, RequestDetail } from "../../../../api/types";
import { AttachmentField } from "../../../../components/AttachmentField";
import { useFailureText } from "../../../../components/errors";
import { Banner, Button, Card, Checkbox, Choice, Field, Loading, Screen, Text, Title } from "../../../../components/ui";
import { useErrorText, useI18n, type MessageKey } from "../../../../i18n/I18nContext";

/** Manual 1.1: the building licence's basic information, kept as a draft until it is submitted. */
export default function BuildingFormScreen() {
  const id = Number(useLocalSearchParams<{ id: string }>().id);
  const { t } = useI18n();
  const failure = useFailureText();
  const [state, setState] = useState<{ request: RequestDetail; establishments: Establishment[] } | { error: unknown } | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([api.getRequest(id), api.myEstablishments()])
      .then(([request, establishments]) => active && setState({ request, establishments }))
      .catch((error) => active && setState({ error }));
    return () => {
      active = false;
    };
  }, [id]);

  if (!state) return <Loading />;
  if ("error" in state) return <Screen><Banner kind="error">{failure(state.error, "loadFailed")}</Banner></Screen>;
  if (!state.request.canEdit || !state.request.building)
    return (
      <Screen>
        <Banner kind="info">{t("requestLocked")}</Banner>
        <Button label={t("viewDetails")} onPress={() => router.replace(`/requests/${id}`)} />
      </Screen>
    );
  return <Editor request={state.request} establishments={state.establishments} />;
}

type TextField = Exclude<keyof BuildingForm, "contacts" | "termsAccepted" | "establishmentId" | "licenceTypeCode">;

function Editor({ request, establishments }: { request: RequestDetail; establishments: Establishment[] }) {
  const { t, pick } = useI18n();
  const errorText = useErrorText();
  const failure = useFailureText();
  const [form, setForm] = useState<BuildingForm>(() => toBuildingForm(request.building!.form));
  const [attachments, setAttachments] = useState<AttachmentInfo[]>(request.attachments);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const [busy, setBusy] = useState<"save" | "submit" | null>(null);

  const set = (field: keyof BuildingForm) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }));
    setErrors(({ [field]: _removed, ...rest }) => rest);
  };
  const setContact = (index: number, field: keyof ContactForm, value: string) => {
    setForm((f) => ({ ...f, contacts: f.contacts.map((c, i) => (i === index ? { ...c, [field]: value } : c)) }));
    setErrors(({ [`contacts[${index}].${field}`]: _removed, ...rest }) => rest);
  };
  // Blank contact rows are not sent, so error indexes match the rows on screen.
  const cleaned = () => ({ ...form, contacts: form.contacts.filter((c) => c.name.trim() || c.email.trim() || c.mobile.trim()) });
  const err = (field: string) => (errors[field]?.[0] ? errorText(errors[field][0]) : null);
  const required = (field: keyof BuildingForm) => (REQUIRED_BUILDING_FIELDS as readonly string[]).includes(field);

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
    const next = cleaned();
    setForm(next);
    const found = validateBuilding(next, "draft");
    if (Object.keys(found).length) return show(found, "saveFailed");
    setBusy("save");
    try {
      await api.saveBuilding(request.id, toBuildingData(next));
      setErrors({});
      setBanner({ kind: "success", text: t("draftSaved") });
    } catch (e) {
      handle(e, "saveFailed");
    } finally {
      setBusy(null);
    }
  }

  async function submit() {
    const next = cleaned();
    setForm(next);
    const found = validateBuilding(next, "submit", request.attachmentSlots, attachments);
    if (Object.keys(found).length) return show(found, "submitFailed");
    setBusy("submit");
    try {
      await api.saveBuilding(request.id, toBuildingData(next));
      await api.submitRequest(request.id);
      router.replace(`/requests/${request.id}`);
    } catch (e) {
      handle(e, "submitFailed");
      setBusy(null);
    }
  }

  const input = (field: TextField, kind: "text" | "number" | "long" = "text", hint?: string) => (
    <Field
      label={t(`field.b.${field}` as MessageKey)}
      required={required(field)}
      value={form[field]}
      onChangeText={set(field)}
      error={err(field)}
      hint={hint}
      multiline={kind === "long"}
      keyboardType={kind === "number" ? "decimal-pad" : "default"}
    />
  );

  return (
    <Screen>
      <Title>{t("buildingTitle")}</Title>
      <Text muted small>{t("requestNumber")}: {request.requestNumber}</Text>
      <Text muted small>{t("requiredHint")}</Text>
      {banner ? <Banner kind={banner.kind}>{banner.text}</Banner> : null}

      <Card>
        <Title level={2}>{t("sectionBasicInfo")}</Title>
        {establishments.length === 0 ? <Text small>{t("noEstablishments")}</Text> : null}
        <Choice
          label={t("field.b.establishmentId")}
          required
          value={form.establishmentId}
          onChange={set("establishmentId")}
          error={err("establishmentId")}
          options={establishments.map((e) => ({ value: String(e.id), label: `${pick(e, "name")} – ${e.crNumber}` }))}
        />
        {input("contractNumber", "text", t("contractNumberHint"))}
        <Choice
          label={t("field.b.licenceTypeCode")}
          required
          value={form.licenceTypeCode}
          onChange={set("licenceTypeCode")}
          error={err("licenceTypeCode")}
          options={LICENCE_TYPES.map((code) => ({ value: code, label: t(`licenceType.${code}`) }))}
        />
        {input("constructionBudgetSar", "number", t("sar"))}
        {input("floorCount", "number")}
        {input("scopeOfWork", "long")}
        {input("notes", "long")}
      </Card>

      <Card>
        <Title level={2}>{t("contacts")}</Title>
        <Text muted small>{t("contactDetails")}</Text>
        {form.contacts.length === 0 ? <Text muted>{t("noContacts")}</Text> : null}
        {form.contacts.map((c, i) => (
          <View key={i} style={{ borderTopWidth: i ? 1 : 0, borderColor: "#d9dee3", paddingTop: i ? 8 : 0 }}>
            <Field label={t("contact.name")} value={c.name} onChangeText={(v) => setContact(i, "name", v)} error={err(`contacts[${i}].name`)} />
            <Field label={t("contact.email")} value={c.email} onChangeText={(v) => setContact(i, "email", v)} keyboardType="email-address" autoCapitalize="none" error={err(`contacts[${i}].email`)} />
            <Field label={t("contact.mobile")} value={c.mobile} onChangeText={(v) => setContact(i, "mobile", v)} keyboardType="phone-pad" error={err(`contacts[${i}].mobile`)} />
            <Button kind="secondary" label={t("removeContact")} onPress={() => setForm((f) => ({ ...f, contacts: f.contacts.filter((_, j) => j !== i) }))} />
          </View>
        ))}
        {form.contacts.length < MAX_CONTACTS ? (
          <Button kind="secondary" label={t("addContact")} onPress={() => setForm((f) => ({ ...f, contacts: [...f.contacts, { name: "", email: "", mobile: "" }] }))} />
        ) : null}
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

      <Checkbox
        label={`* ${t("termsAccepted")}`}
        checked={form.termsAccepted}
        onChange={(v) => {
          setForm((f) => ({ ...f, termsAccepted: v }));
          setErrors(({ termsAccepted: _removed, ...rest }) => rest);
        }}
        error={err("termsAccepted")}
      />
      <Button label={busy === "submit" ? t("submitting") : t("submitRequest")} busy={busy === "submit"} disabled={busy !== null} onPress={() => void submit()} />
      <Button kind="secondary" label={busy === "save" ? t("savingDraft") : t("saveDraft")} busy={busy === "save"} disabled={busy !== null} onPress={() => void save()} />
    </Screen>
  );
}
