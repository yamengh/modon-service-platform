import { api } from "../api/client";
import type { LookupCategory } from "../api/types";
import { contractor, establishments, lookups } from "./render";

/**
 * Default backend responses for request pages. Test files must mock "../api/client" with every
 * api function replaced by vi.fn() (see the vi.mock call at the top of each request test file).
 */
export function useExcavationBackend() {
  vi.mocked(api.getPersonas).mockResolvedValue(contractor);
  vi.mocked(api.myEstablishments).mockResolvedValue(establishments);
  vi.mocked(api.listLookups).mockImplementation(async (category: LookupCategory) => lookups[category]);
  vi.mocked(api.attachmentUrl).mockImplementation((id: number, slot: string) => `/api/modon/requests/${id}/attachments/${slot}`);
}

/** A File whose reported size can be set without allocating that many bytes. */
export function fakeFile(name: string, type: string, size = 1024): File {
  const file = new File(["%PDF-1.4"], name, { type });
  Object.defineProperty(file, "size", { value: size });
  return file;
}
