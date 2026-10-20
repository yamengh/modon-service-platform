import type {
  FieldErrors,
  LookupCategory,
  LookupOption,
  Me,
  QueueFilters,
  ReviewHistoryItem,
  ReviewQueuePage,
  ReviewRequestDetail,
  ReviewSummary,
  RequestWorkflowReport,
  WorkflowStatusReport,
  ServiceSummary,
} from "./types";

/**
 * An HTTP error from the backend. Errors look like { title, code };
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
const CLIENT_HEADER = { "X-Modon-Client": "admin" };

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

async function json<T>(res: Response): Promise<T> {
  const text = await res.text();
  return (text ? JSON.parse(text) : null) as T;
}

const reviewUrl = (id: number) => `/api/modon/review/requests/${id}`;
const post = (body: object): RequestInit => ({ method: "POST", body: JSON.stringify(body) });

export const api = {
  /** FormCMS login: sets the auth cookie. */
  async login(email: string, password: string): Promise<void> {
    await request("/api/login", { method: "POST", body: JSON.stringify({ usernameOrEmail: email, password }) });
  },

  async logout(): Promise<void> {
    await request("/api/logout");
  },

  /** Returns null when nobody is signed in. */
  async getMe(): Promise<Me | null> {
    try {
      return await json<Me>(await request("/api/modon/me/personas"));
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return null;
      throw e;
    }
  },

  async getSummary(): Promise<ReviewSummary> {
    return json(await request("/api/modon/review/summary"));
  },

  /** Which workflow engine is configured, and whether it works right now. */
  async getWorkflowStatus(): Promise<WorkflowStatusReport> {
    return json(await request("/api/modon/review/workflow"));
  },

  /** Compares the request's status with its Camunda process instance. */
  async getRequestWorkflow(id: number): Promise<RequestWorkflowReport> {
    return json(await request(`${reviewUrl(id)}/workflow`));
  },

  async listRequests(filters: QueueFilters): Promise<ReviewQueuePage> {
    const params = new URLSearchParams();
    if (filters.status) params.set("status", filters.status);
    if (filters.service) params.set("service", filters.service);
    if (filters.search) params.set("search", filters.search);
    if (filters.page && filters.page > 1) params.set("page", String(filters.page));
    const query = params.toString();
    return json(await request(`/api/modon/review/requests${query ? `?${query}` : ""}`));
  },

  async getRequest(id: number): Promise<ReviewRequestDetail> {
    return json(await request(reviewUrl(id)));
  },

  async getHistory(id: number): Promise<ReviewHistoryItem[]> {
    return json(await request(`${reviewUrl(id)}/history`));
  },

  async approve(id: number, comment?: string): Promise<ReviewRequestDetail> {
    return json(await request(`${reviewUrl(id)}/approve`, post({ comment: comment || null })));
  },

  async returnForCompletion(id: number, comment: string): Promise<ReviewRequestDetail> {
    return json(await request(`${reviewUrl(id)}/return`, post({ comment })));
  },

  async reject(id: number, reason: string): Promise<ReviewRequestDetail> {
    return json(await request(`${reviewUrl(id)}/reject`, post({ reason })));
  },

  /** Asks the applicant for the coordination letters (instructions are optional). */
  async requestCoordinationLetters(id: number, comment?: string): Promise<ReviewRequestDetail> {
    return json(await request(`${reviewUrl(id)}/request-coordination-letters`, post({ comment: comment || null })));
  },

  /** Download link; the backend checks the reviewer role. */
  attachmentUrl(id: number, slot: string): string {
    return `${reviewUrl(id)}/attachments/${encodeURIComponent(slot)}`;
  },

  /** Building licence: an office's quotation attachment or a progress-report attachment. */
  buildingFileUrl(id: number, kind: "quotations" | "progress-reports", fileId: number): string {
    return `${reviewUrl(id)}/building/${kind}/${fileId}/attachment`;
  },

  /** Catalogue services (FormCMS saved query), for the service filter. */
  async listServices(): Promise<ServiceSummary[]> {
    return (await json<ServiceSummary[] | null>(await request("/api/queries/modonServices"))) ?? [];
  },

  /** Dropdown options (FormCMS saved query), to show names instead of codes. */
  async listLookups(category: LookupCategory): Promise<LookupOption[]> {
    return (await json<LookupOption[] | null>(await request(`/api/queries/modonLookups?category=${category}`))) ?? [];
  },
};
