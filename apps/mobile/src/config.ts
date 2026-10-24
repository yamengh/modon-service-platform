/**
 * The Modon backend address as seen from the device, from EXPO_PUBLIC_API_BASE_URL (.env.local, see .env.example).
 * There is no built-in default on purpose: "localhost" means the phone itself on a device, so a wrong guess
 * would fail silently. Only public, non-secret settings may use the EXPO_PUBLIC_ prefix (they are bundled).
 */
export function apiBaseUrl(): string | null {
  const raw = process.env.EXPO_PUBLIC_API_BASE_URL?.trim();
  if (!raw) return null;
  try {
    const url = new URL(raw);
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    return raw.replace(/\/+$/, "");
  } catch {
    return null;
  }
}
