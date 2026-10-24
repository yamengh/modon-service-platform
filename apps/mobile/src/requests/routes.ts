import { OPEN_SERVICES } from "@portal/requests/serviceAvailability";
import type { ApplicantTaskKind, Persona, RequestStatus } from "../api/types";

/** Who may start each service — the portal's list (the backend enforces it again: 403). */
export function canStart(serviceCode: string, personas: Persona[]): boolean {
  const open = OPEN_SERVICES[serviceCode];
  return !!open && open.personas.some((p) => personas.includes(p));
}

export const isOpenService = (serviceCode: string) => !!OPEN_SERVICES[serviceCode];

export function formRoute(serviceCode: string, id: number): string {
  return serviceCode === "building-licence" ? `/requests/${id}/building` : `/requests/${id}/excavation`;
}

/** Where to act on a request: the form (draft / returned), the coordination letters, or nowhere. */
export function actionRoute(serviceCode: string, id: number, status: RequestStatus): string | null {
  if (!isOpenService(serviceCode)) return null;
  if (status === "draft" || status === "returnedForCompletion") return formRoute(serviceCode, id);
  if (status === "awaitingCoordinationLetters") return `/requests/${id}/letters`;
  return null;
}

/** Offices answer on the project screen; applicants act on their request. */
export function taskRoute(task: { kind: ApplicantTaskKind; serviceCode: string; requestId: number; status: RequestStatus }): string {
  if (task.kind === "respondAsConsultant" || task.kind === "respondAsContractor") return `/projects/${task.requestId}`;
  return actionRoute(task.serviceCode, task.requestId, task.status) ?? `/requests/${task.requestId}`;
}
