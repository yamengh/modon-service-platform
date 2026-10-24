import type { ReactNode } from "react";
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text as RNText,
  TextInput,
  View,
  type TextInputProps,
  type TextProps,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import type { RequestStatus } from "../api/types";
import { useI18n } from "../i18n/I18nContext";

export const colors = {
  green: "#0b7a4b",
  greenDark: "#075c38",
  ink: "#1d2730",
  muted: "#5d6b78",
  line: "#d9dee3",
  bg: "#f4f6f8",
  white: "#ffffff",
  danger: "#b42318",
  dangerBg: "#fdecea",
  warn: "#9a5b00",
  warnBg: "#fff4e0",
  blue: "#1f5f8b",
  blueBg: "#e6f0f6",
  purple: "#5b3e96",
  purpleBg: "#f1ecfa",
  noticeBg: "#e8f3ed",
};

/** Text aligned with the reading direction (Arabic: right-to-left). */
export function Text({ style, muted, bold, small, ...props }: TextProps & { muted?: boolean; bold?: boolean; small?: boolean }) {
  const { rtl } = useI18n();
  return (
    <RNText
      {...props}
      style={[
        { color: muted ? colors.muted : colors.ink, textAlign: rtl ? "right" : "left", writingDirection: rtl ? "rtl" : "ltr", fontSize: small ? 13 : 15 },
        bold && { fontWeight: "700" },
        style,
      ]}
    />
  );
}

export function Title({ children, level = 1 }: { children: ReactNode; level?: 1 | 2 | 3 }) {
  return (
    <Text accessibilityRole="header" bold style={{ fontSize: level === 1 ? 22 : level === 2 ? 18 : 16, marginVertical: 6 }}>
      {children}
    </Text>
  );
}

/** A horizontal row that follows the reading direction. */
export function Row({ children, style, gap = 8 }: { children: ReactNode; style?: ViewStyle; gap?: number }) {
  const { rtl } = useI18n();
  return <View style={[{ flexDirection: rtl ? "row-reverse" : "row", alignItems: "center", gap, flexWrap: "wrap" }, style]}>{children}</View>;
}

/** Scrollable page with safe-area padding and optional pull-to-refresh. */
export function Screen({ children, refreshing, onRefresh }: { children: ReactNode; refreshing?: boolean; onRefresh?: () => void }) {
  return (
    <SafeAreaView style={styles.screen} edges={["bottom"]}>
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        refreshControl={onRefresh ? <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} /> : undefined}
      >
        {children}
      </ScrollView>
    </SafeAreaView>
  );
}

export function Card({ children, style, accent }: { children: ReactNode; style?: ViewStyle; accent?: string }) {
  const { rtl } = useI18n();
  return (
    <View style={[styles.card, accent ? { [rtl ? "borderRightWidth" : "borderLeftWidth"]: 4, [rtl ? "borderRightColor" : "borderLeftColor"]: accent } : null, style]}>
      {children}
    </View>
  );
}

type ButtonKind = "primary" | "secondary" | "danger";

/** At least 48 dp high, labelled for screen readers, disabled while busy. */
export function Button({
  label, onPress, kind = "primary", disabled, busy, accessibilityHint,
}: { label: string; onPress: () => void; kind?: ButtonKind; disabled?: boolean; busy?: boolean; accessibilityHint?: string }) {
  const off = disabled || busy;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled: !!off, busy: !!busy }}
      disabled={off}
      onPress={onPress}
      style={({ pressed }) => [styles.button, styles[kind], off && styles.disabled, pressed && !off && { opacity: 0.85 }]}
    >
      {busy ? <ActivityIndicator color={kind === "secondary" ? colors.green : colors.white} /> : null}
      <RNText style={[styles.buttonText, kind === "secondary" && { color: colors.ink }]}>{label}</RNText>
    </Pressable>
  );
}

/** A labelled text input; the label is also the accessibility label, and the error is announced. */
export function Field({
  label, value, onChangeText, error, required, hint, multiline, ...props
}: Omit<TextInputProps, "style"> & { label: string; error?: string | null; required?: boolean; hint?: string }) {
  const { rtl } = useI18n();
  return (
    <View style={styles.field}>
      <Text bold small>
        {required ? <RNText style={{ color: colors.danger }}>* </RNText> : null}
        {label}
      </Text>
      <TextInput
        {...props}
        accessibilityLabel={label}
        accessibilityHint={hint}
        value={value}
        onChangeText={onChangeText}
        multiline={multiline}
        placeholderTextColor={colors.muted}
        style={[styles.input, multiline && { minHeight: 96, textAlignVertical: "top" }, { textAlign: rtl ? "right" : "left" }, error ? { borderColor: colors.danger } : null]}
      />
      {hint ? <Text muted small>{hint}</Text> : null}
      {error ? <Text small style={{ color: colors.danger }} accessibilityLiveRegion="polite">{error}</Text> : null}
    </View>
  );
}

/** A small set of choices shown as radio buttons (React Native has no native dropdown). */
export function Choice<T extends string>({
  label, value, options, onChange, error, required,
}: { label: string; value: T | ""; options: { value: T; label: string }[]; onChange: (v: T) => void; error?: string | null; required?: boolean }) {
  return (
    <View style={styles.field} accessibilityRole="radiogroup" accessibilityLabel={label}>
      <Text bold small>
        {required ? <RNText style={{ color: colors.danger }}>* </RNText> : null}
        {label}
      </Text>
      {options.map((o) => {
        const selected = o.value === value;
        return (
          <Pressable
            key={o.value}
            accessibilityRole="radio"
            accessibilityLabel={o.label}
            accessibilityState={{ checked: selected }}
            onPress={() => onChange(o.value)}
            style={[styles.option, selected && styles.optionSelected]}
          >
            <Row>
              <View style={[styles.radio, selected && styles.radioOn]} />
              <Text style={{ flexShrink: 1 }}>{o.label}</Text>
            </Row>
          </Pressable>
        );
      })}
      {error ? <Text small style={{ color: colors.danger }}>{error}</Text> : null}
    </View>
  );
}

export function Checkbox({ label, checked, onChange, error }: { label: string; checked: boolean; onChange: (v: boolean) => void; error?: string | null }) {
  return (
    <View style={styles.field}>
      <Pressable accessibilityRole="checkbox" accessibilityLabel={label} accessibilityState={{ checked }} onPress={() => onChange(!checked)} style={styles.option}>
        <Row>
          <View style={[styles.checkbox, checked && styles.checkboxOn]}>{checked ? <RNText style={{ color: colors.white }}>✓</RNText> : null}</View>
          <Text style={{ flexShrink: 1 }}>{label}</Text>
        </Row>
      </Pressable>
      {error ? <Text small style={{ color: colors.danger }}>{error}</Text> : null}
    </View>
  );
}

export function Banner({ kind, children }: { kind: "error" | "success" | "info"; children: ReactNode }) {
  const palette = kind === "error" ? [colors.dangerBg, colors.danger] : kind === "success" ? [colors.noticeBg, colors.greenDark] : [colors.blueBg, colors.blue];
  return (
    <View style={[styles.banner, { backgroundColor: palette[0] }]} accessibilityRole={kind === "error" ? "alert" : undefined} accessibilityLiveRegion="polite">
      <Text style={{ color: palette[1] }}>{children}</Text>
    </View>
  );
}

export function Loading() {
  const { t } = useI18n();
  return (
    <View style={{ padding: 32, alignItems: "center", gap: 8 }} accessibilityLabel={t("loading")}>
      <ActivityIndicator color={colors.green} />
      <Text muted>{t("loading")}</Text>
    </View>
  );
}

export function Empty({ text }: { text: string }) {
  return (
    <View style={{ padding: 24 }}>
      <Text muted style={{ textAlign: "center" }}>{text}</Text>
    </View>
  );
}

const STATUS_COLORS: Partial<Record<RequestStatus, [string, string]>> = {
  inProgress: [colors.blueBg, colors.blue],
  underModonReview: [colors.blueBg, colors.blue],
  approved: [colors.noticeBg, colors.greenDark],
  rejected: [colors.dangerBg, colors.danger],
  cancelled: [colors.dangerBg, colors.danger],
  returnedForCompletion: [colors.warnBg, colors.warn],
  awaitingCoordinationLetters: [colors.warnBg, colors.warn],
  consultantSelection: [colors.warnBg, colors.warn],
  contractorSelection: [colors.warnBg, colors.warn],
  consultantTender: [colors.purpleBg, colors.purple],
  contractorTender: [colors.purpleBg, colors.purple],
  awaitingConsultantDecision: [colors.purpleBg, colors.purple],
  awaitingContractorDecision: [colors.purpleBg, colors.purple],
};

export function StatusBadge({ status }: { status: RequestStatus }) {
  const { t } = useI18n();
  const [bg, fg] = STATUS_COLORS[status] ?? ["#eef1f4", colors.muted];
  return (
    <View style={{ backgroundColor: bg, borderRadius: 999, paddingHorizontal: 10, paddingVertical: 3, alignSelf: "flex-start" }}>
      <RNText style={{ color: fg, fontWeight: "700", fontSize: 12 }}>{t(`status.${status}`)}</RNText>
    </View>
  );
}

/** A label/value pair for read-only details. */
export function Detail({ label, value }: { label: string; value: ReactNode }) {
  return (
    <View style={{ marginBottom: 8 }}>
      <Text muted small>{label}</Text>
      {typeof value === "string" || typeof value === "number" ? <Text>{value}</Text> : value}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.bg },
  content: { padding: 16, gap: 12, paddingBottom: 48 },
  card: { backgroundColor: colors.white, borderRadius: 10, borderWidth: 1, borderColor: colors.line, padding: 14, gap: 6 },
  button: { minHeight: 48, borderRadius: 8, paddingHorizontal: 16, alignItems: "center", justifyContent: "center", flexDirection: "row", gap: 8, borderWidth: 1, borderColor: "transparent" },
  primary: { backgroundColor: colors.green },
  secondary: { backgroundColor: colors.white, borderColor: colors.line },
  danger: { backgroundColor: colors.danger },
  disabled: { opacity: 0.5 },
  buttonText: { color: colors.white, fontWeight: "700", fontSize: 15 },
  field: { gap: 4, marginBottom: 10 },
  input: { minHeight: 48, borderWidth: 1, borderColor: colors.line, borderRadius: 8, paddingHorizontal: 12, paddingVertical: 10, backgroundColor: colors.white, fontSize: 15, color: colors.ink },
  option: { minHeight: 48, justifyContent: "center", paddingHorizontal: 10, borderRadius: 8, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.white, marginTop: 4 },
  optionSelected: { borderColor: colors.green, backgroundColor: colors.noticeBg },
  radio: { width: 20, height: 20, borderRadius: 10, borderWidth: 2, borderColor: colors.muted },
  radioOn: { borderColor: colors.green, backgroundColor: colors.green },
  checkbox: { width: 24, height: 24, borderRadius: 4, borderWidth: 2, borderColor: colors.muted, alignItems: "center", justifyContent: "center" },
  checkboxOn: { backgroundColor: colors.green, borderColor: colors.green },
  banner: { borderRadius: 8, padding: 12 },
});
