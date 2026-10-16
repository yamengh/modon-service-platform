import type { Persona, RequestStatus } from "../api/types";

/**
 * Services whose application form exists in the portal, and who may apply.
 * Mirrors the backend's ServiceApplications; the backend still enforces it (403 / 400).
 */
export const OPEN_SERVICES: Record<
  string,
  { personas: Persona[]; formPath: (id: number) => string; lettersPath: (id: number) => string }
> = {
  "excavation-permit": {
    personas: ["contractor"],
    formPath: (id) => `/requests/${id}/edit`,
    lettersPath: (id) => `/requests/${id}/coordination-letters`,
  },
};

/** Where to go to act on a request: the form (draft / returned) or the coordination-letters page. */
export function actionPath(serviceCode: string, id: number, status: RequestStatus): string | null {
  const service = OPEN_SERVICES[serviceCode];
  if (!service) return null;
  if (status === "draft" || status === "returnedForCompletion") return service.formPath(id);
  if (status === "awaitingCoordinationLetters") return service.lettersPath(id);
  return null;
}
