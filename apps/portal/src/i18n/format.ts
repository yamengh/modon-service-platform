import { messages, type Lang, type MessageKey } from "./messages";

/** Replaces {name} placeholders, e.g. fill("{count} fields", { count: 3 }). */
export function fill(text: string, vars: Record<string, string | number>): string {
  return text.replace(/\{(\w+)\}/g, (_, name: string) => String(vars[name] ?? `{${name}}`));
}

/** Message key for a backend/client error code, falling back to a generic message. */
export function errorKey(code: string): MessageKey {
  const key = `err.${code}`;
  return key in messages.ar ? (key as MessageKey) : "genericError";
}

const pad = (n: number) => String(n).padStart(2, "0");

/** dd/MM/yyyy with Latin digits, as in the Modon portal screenshots. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`;
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return `${formatDate(iso)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** "yyyy-MM-dd" (form date) → dd/MM/yyyy without timezone shifts. */
export function formatPlainDate(value: string | null | undefined): string {
  if (!value) return "—";
  const [y, m, d] = value.split("-");
  return `${d}/${m}/${y}`;
}

export function formatSize(bytes: number, lang: Lang): string {
  const kb = Math.max(1, Math.round(bytes / 1024));
  return kb >= 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${kb} ${lang === "ar" ? "ك.ب" : "KB"}`;
}
