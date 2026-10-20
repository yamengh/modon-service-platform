import type { ApplicantTaskKind, Persona, RequestStatus } from "../api/types";

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
  // The investor's later steps (consultant, contractor) happen on the request page itself.
  "building-licence": {
    personas: ["investor"],
    formPath: (id) => `/requests/${id}/building`,
    lettersPath: (id) => `/requests/${id}`,
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

/** Where a task leads: offices answer on the project page, investors act on the request page. */
export function taskPath(task: { kind: ApplicantTaskKind; serviceCode: string; requestId: number; status: RequestStatus }): string {
  if (task.kind === "respondAsConsultant" || task.kind === "respondAsContractor") return `/projects/${task.requestId}`;
  return actionPath(task.serviceCode, task.requestId, task.status) ?? `/requests/${task.requestId}`;
}
