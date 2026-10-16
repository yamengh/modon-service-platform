import type {
  AttachmentInfo,
  Establishment,
  ExcavationData,
  FieldErrors,
  LookupCategory,
  LookupOption,
  PersonaResponse,
  RequestDetail,
  RequestSummary,
  ServiceDetail,
  ServiceSummary,
  TaskItem,
} from "./types";

/**
 * An HTTP error from the backend. FormCMS and Modon errors look like { title, code };
 * validation failures (400) also carry "errors": field → error codes.
 */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors: FieldErrors = {},
  ) {
    super(message);
  }
}

/** Required by the backend on state-changing /api/modon calls (CSRF protection). */
const CLIENT_HEADER = { "X-Modon-Client": "portal" };

async function request(path: string, init?: RequestInit): Promise<Response> {
  const isJson = typeof init?.body === "string";
  const res = await fetch(path, {
    credentials: "same-origin",
    ...init,
    headers: { Accept: "application/json", ...CLIENT_HEADER, ...(isJson ? { "Content-Type": "application/json" } : {}) },
  });
  if (!res.ok) {
    let message = res.statusText;
    let errors: FieldErrors = {};
    try {
      const body = await res.json();
      message = body?.title ?? message;
      errors = body?.errors ?? {};
    } catch {
      // response had no JSON body
    }
    throw new ApiError(res.status, message, errors);
  }
  return res;
}

/** Parses JSON, treating an empty body as null (FormCMS single queries return empty when nothing matches). */
async function jsonOrNull<T>(res: Response): Promise<T | null> {
  const text = await res.text();
  return text && text !== "null" ? (JSON.parse(text) as T) : null;
}

async function json<T>(res: Response): Promise<T> {
  return (await res.json()) as T;
}

const requestUrl = (id: number) => `/api/modon/requests/${id}`;

export const api = {
  /** FormCMS login: sets the auth cookie. */
  async login(email: string, password: string): Promise<void> {
    await request("/api/login", {
      method: "POST",
      body: JSON.stringify({ usernameOrEmail: email, password }),
    });
  },

  async logout(): Promise<void> {
    await request("/api/logout");
  },

  /** Returns null when nobody is signed in. */
  async getPersonas(): Promise<PersonaResponse | null> {
    try {
      return await jsonOrNull<PersonaResponse>(await request("/api/modon/me/personas"));
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return null;
      throw e;
    }
  },

  /** FormCMS saved query, ordered by sortOrder. */
  async listServices(): Promise<ServiceSummary[]> {
    return (await jsonOrNull<ServiceSummary[]>(await request("/api/queries/modonServices"))) ?? [];
  },

  /** Returns null when no service has this code. */
  async getService(code: string): Promise<ServiceDetail | null> {
    return jsonOrNull<ServiceDetail>(
      await request(`/api/queries/modonService/single?code=${encodeURIComponent(code)}`),
    );
  },

  /** Dropdown options (FormCMS saved query), in display order. */
  async listLookups(category: LookupCategory): Promise<LookupOption[]> {
    return (
      (await jsonOrNull<LookupOption[]>(
        await request(`/api/queries/modonLookups?category=${encodeURIComponent(category)}`),
      )) ?? []
    );
  },

  /** Establishments (commercial registrations) the signed-in user may act for. */
  async myEstablishments(): Promise<Establishment[]> {
    return json(await request("/api/modon/me/establishments"));
  },

  async createRequest(serviceCode: string): Promise<RequestDetail> {
    return json(await request("/api/modon/requests", { method: "POST", body: JSON.stringify({ serviceCode }) }));
  },

  async listMyRequests(): Promise<RequestSummary[]> {
    return json(await request("/api/modon/requests"));
  },

  async getRequest(id: number): Promise<RequestDetail> {
    return json(await request(requestUrl(id)));
  },

  /** Saves a draft. The backend checks formats but allows missing fields. */
  async saveExcavation(id: number, data: ExcavationData): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/excavation`, { method: "PUT", body: JSON.stringify(data) }));
  },

  /** Submits a draft, or resubmits a request that Modon returned for completion. */
  async submitRequest(id: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/submit`, { method: "POST" }));
  },

  /** Requests waiting for the signed-in user (returned for completion, coordination letters requested). */
  async listTasks(): Promise<TaskItem[]> {
    return json(await request("/api/modon/tasks"));
  },

  /** Sends the uploaded coordination letters; the backend requires all of them. */
  async submitCoordinationLetters(id: number): Promise<RequestDetail> {
    return json(await request(`${requestUrl(id)}/coordination-letters/submit`, { method: "POST" }));
  },

  async uploadAttachment(id: number, slot: string, file: File): Promise<AttachmentInfo> {
    const form = new FormData();
    form.append("file", file);
    return json(await request(`${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`, { method: "POST", body: form }));
  },

  async deleteAttachment(id: number, slot: string): Promise<void> {
    await request(`${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`, { method: "DELETE" });
  },

  /** Download link; the backend checks that the signed-in user owns the request. */
  attachmentUrl(id: number, slot: string): string {
    return `${requestUrl(id)}/attachments/${encodeURIComponent(slot)}`;
  },
};
