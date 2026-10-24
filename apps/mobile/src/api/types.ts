/**
 * The API contract is the portal's: same backend, same JSON. Re-exported (types only, erased at build time)
 * so the app cannot drift from the web portal.
 */
export type * from "@portal/api/types";

/** POST /api/modon/mobile/session */
export interface MobileLoginResponse {
  token: string;
  expiresAt: string;
  user: import("@portal/api/types").PersonaResponse;
}

/** A file picked on the device, ready to upload. */
export interface PickedFile {
  uri: string;
  name: string;
  size: number | null;
  mimeType: string | null;
}
