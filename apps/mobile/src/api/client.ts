import { File, Paths } from "expo-file-system";
import * as Sharing from "expo-sharing";
import { apiBaseUrl } from "../config";
import type {
  AttachmentInfo,
  BuildingLicenceData,
  BuildingStage,
  Establishment,
  ExcavationData,
  FieldErrors,
  LookupCategory,
  LookupOption,
  MobileLoginResponse,
  NotificationPage,
  OfficeInfo,
  OfficeResponse,
  PartnerProjectDetail,
  PartnerProjectSummary,
  PersonaResponse,
  PickedFile,
  ProgressReportInput,
  RequestDetail,
  RequestSummary,
  SelectionMode,
  ServiceDetail,
  ServiceSummary,
  TaskItem,
} from "./types";

/**
 * An API failure. `status` 0 means the backend could not be reached (offline, wrong address);
 * otherwise `message` is the backend's problem title (e.g. "requestNotEditable") and `errors` its field errors.
 */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors: FieldErrors = {},
  ) {
    super(message);
  }

  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: (() => void) | null = null;

/** The bearer token for the signed-in user (kept in memory; the auth context persists it in SecureStore). */
export function setSessionToken(value: string | null) {
  token = value;
}

/** Called when the backend rejects the token (expired, revoked): the app signs the user out. */
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

function base(): string {
  const url = apiBaseUrl();
  if (!url) throw new ApiError(0, "apiNotConfigured");
  return url;
}

/** Same headers as the web portal's client, plus the bearer token instead of a cookie. */
function headers(extra: Record<string, string> = {}): Record<string, string> {
  return {
    Accept: "application/json",
    "X-Modon-Client": "mobile",
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra,
  };
}

async function request(path: string, init: RequestInit = {}, authenticated = true): Promise<Response> {
  const isJson = typeof init.body === "string";
  let res: Response;
  try {
    res = await fetch(base() + path, {
      ...init,
      headers: headers(isJson ? { "Content-Type": "application/json" } : {}),
    });
  } catch (e) {
    if (e instanceof ApiError) throw e;
    throw new ApiError(0, "network");
  }
  if (!res.ok) {
    let message = res.statusText || String(res.status);
    let errors: FieldErrors = {};
    try {
      const body = await res.json();
      message = body?.title ?? message;
      errors = body?.errors ?? {};
    } catch {
      // no JSON body
    }
    if (res.status === 401 && authenticated && token) onUnauthorized?.();
    throw new ApiError(res.status, message, errors);
  }
  return res;
}

async function json<T>(res: Response): Promise<T> {
  const text = await res.text();
  return (text && text !== "null" ? JSON.parse(text) : null) as T;
}

const CONTENT_TYPES: Record<string, string> = {
  ".jpeg": "image/jpeg",
  ".jpg": "image/jpeg",
  ".png": "image/png",
  ".pdf": "application/pdf",
  ".doc": "application/msword",
  ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  ".xls": "application/vnd.ms-excel",
  ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
};

export function extensionOf(name: string): string {
  const dot = name.lastIndexOf(".");
  return dot >= 0 ? name.slice(dot).toLowerCase() : "";
}

/**
 * FormData file part: the backend checks the extension, the declared type and the content.
 * Expo's global fetch (SDK 52+) cannot send React Native's `{ uri }` parts and reads the content through `bytes()`;
 * `uri` stays for React Native's own fetch/XMLHttpRequest.
 */
function filePart(file: PickedFile) {
  return {
    uri: file.uri,
    name: file.name,
    type: CONTENT_TYPES[extensionOf(file.name)] ?? "application/octet-stream",
    bytes: () => new File(file.uri).bytes(),
  };
}

function formWith(field: string, file: PickedFile, data?: object): FormData {
  const form = new FormData();
  if (data) form.append("data", JSON.stringify(data));
  form.append(field, filePart(file) as unknown as Blob);
  return form;
}

const requestUrl = (id: number) => `/api/modon/requests/${id}`;
const post = (body?: object): RequestInit => ({ method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });

export const api = {
  // --- session ---------------------------------------------------------------------------------
  async login(email: string, password: string, deviceName: string): Promise<MobileLoginResponse> {
    return json(await request("/api/modon/mobile/session", post({ email, password, deviceName }), false));
  },
  async logout(): Promise<void> {
    await request("/api/modon/mobile/session", { method: "DELETE" }, false);
  },
  async me(): Promise<PersonaResponse> {
    return json(await request("/api/modon/me/personas"));
  },

  // --- catalogue and options (FormCMS saved queries) --------------------------------------------
  async listServices(): Promise<ServiceSummary[]> {
    return (await json<ServiceSummary[] | null>(await request("/api/queries/modonServices"))) ?? [];
  },
  async getService(code: string): Promise<ServiceDetail | null> {
    return json(await request(`/api/queries/modonService/single?code=${encodeURIComponent(code)}`));
  },
  async listLookups(category: LookupCategory): Promise<LookupOption[]> {
    return (await json<LookupOption[] | null>(await request(`/api/queries/modonLookups?category=${encodeURIComponent(category)}`))) ?? [];
  },
  async myEstablishments(): Promise<Establishment[]> {
    return json(await request("/api/modon/me/establishments"));
  },

  // --- requests ----------------------------------------------------------------------------------
  async createRequest(serviceCode: string): Promise<RequestDetail> {
    return json(await request("/api/modon/requests", post({ serviceCode })));
  },
  async listMyRequests(): Promise<RequestSummary[]> {
    return json(await request("/api/modon/requests"));
  },
  async getRequest(id: number): Promise<RequestDetail> {
    return json(await request(requestUrl(id)));
  },
  async saveExcavation(id: number, data: ExcavationData): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/excavation`, { method: "PUT", body: JSON.stringify(data) }));
  },
  async saveBuilding(id: number, data: BuildingLicenceData): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/building`, { method: "PUT", body: JSON.stringify(data) }));
  },
  async submitRequest(id: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/submit`, post()));
  },
  async submitCoordinationLetters(id: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/coordination-letters/submit`, post()));
  },
  async listTasks(): Promise<TaskItem[]> {
    return json(await request("/api/modon/tasks"));
  },
  async uploadAttachment(id: number, slot: string, file: PickedFile): Promise<AttachmentInfo> {
    return json(await request(`${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`, { method: "POST", body: formWith("file", file) }));
  },
  async deleteAttachment(id: number, slot: string): Promise<void> {
    await request(`${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`, { method: "DELETE" });
  },

  // --- building licence: investor -----------------------------------------------------------------
  async chooseOffice(id: number, stage: BuildingStage, mode: SelectionMode, establishmentId?: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/building/${stage}/selection`, post({ mode, establishmentId: establishmentId ?? null })));
  },
  async acceptQuotation(id: number, stage: BuildingStage, quotationId: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/building/${stage}/quotations/${quotationId}/accept`, post()));
  },
  async rejectQuotations(id: number, stage: BuildingStage, comment?: string): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/building/${stage}/quotations/reject-all`, post({ comment: comment || null })));
  },
  async listOffices(stage: BuildingStage): Promise<OfficeInfo[]> {
    return json(await request(`/api/modon/building/offices?stage=${stage}`));
  },

  // --- building licence: offices -------------------------------------------------------------------
  async listProjects(): Promise<PartnerProjectSummary[]> {
    return json(await request("/api/modon/building/projects"));
  },
  async getProject(id: number): Promise<PartnerProjectDetail> {
    return json(await request(`/api/modon/building/projects/${id}`));
  },
  async submitQuotation(id: number, establishmentId: number, amountSar: number, comment: string | null): Promise<PartnerProjectDetail> {
    return json(await request(`/api/modon/building/projects/${id}/quotations`, post({ establishmentId, amountSar, comment })));
  },
  /** Returns null after a rejection (the office loses access to the project). */
  async respondToInvitation(id: number, body: OfficeResponse, file?: PickedFile | null): Promise<PartnerProjectDetail | null> {
    const init = file ? { method: "POST", body: formWith("file", file, body) } : post(body);
    return json(await request(`/api/modon/building/projects/${id}/response`, init));
  },
  async submitProgressReport(id: number, body: ProgressReportInput, file?: PickedFile | null): Promise<PartnerProjectDetail> {
    const init = file ? { method: "POST", body: formWith("file", file, body) } : post(body);
    return json(await request(`/api/modon/building/projects/${id}/progress-reports`, init));
  },

  // --- notifications -------------------------------------------------------------------------------
  async listNotifications(page = 1, unreadOnly = false): Promise<NotificationPage> {
    return json(await request(`/api/modon/notifications?page=${page}${unreadOnly ? "&unreadOnly=true" : ""}`));
  },
  async unreadNotifications(): Promise<number> {
    return (await json<{ unreadCount: number }>(await request("/api/modon/notifications/unread-count"))).unreadCount;
  },
  async markNotificationRead(id: number): Promise<void> {
    await request(`/api/modon/notifications/${id}/read`, post());
  },
  async markAllNotificationsRead(): Promise<void> {
    await request("/api/modon/notifications/read-all", post());
  },

  /**
   * Opens a protected document: downloads it with the bearer token into the app's private cache, then hands it to
   * the system viewer/share sheet. Documents never get a public URL.
   */
  async openProtectedFile(path: string, fileName: string): Promise<void> {
    const target = new File(Paths.cache, `${Date.now()}-${fileName.replace(/[^\w.؀-ۿ-]+/g, "_")}`);
    const task = File.createDownloadTask(base() + path, target, { headers: headers() });
    let file: File | null;
    try {
      file = await task.downloadAsync();
    } catch {
      throw new ApiError(0, "network");
    }
    if (!file) throw new ApiError(404, "notFound");
    if (await Sharing.isAvailableAsync()) await Sharing.shareAsync(file.uri);
  },
};

/** Paths of protected downloads (same endpoints and checks as the web portal). */
export const files = {
  attachment: (id: number, slot: string) => `${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`,
  building: (id: number, kind: "quotations" | "progress-reports", fileId: number) => `${requestUrl(id)}/building/${kind}/${fileId}/attachment`,
  projectAttachment: (id: number, slot: string) => `/api/modon/building/projects/${id}/attachments/${encodeURIComponent(slot)}`,
  project: (id: number, kind: "quotations" | "progress-reports", fileId: number) => `/api/modon/building/projects/${id}/${kind}/${fileId}/attachment`,
};
