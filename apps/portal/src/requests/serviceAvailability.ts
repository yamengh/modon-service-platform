import type { Persona } from "../api/types";

/**
 * Services whose application form exists in the portal, and who may apply.
 * Mirrors the backend's ServiceApplications; the backend still enforces it (403 / 400).
 */
export const OPEN_SERVICES: Record<string, { personas: Persona[]; formPath: (id: number) => string }> = {
  "excavation-permit": { personas: ["contractor"], formPath: (id) => `/requests/${id}/edit` },
};
