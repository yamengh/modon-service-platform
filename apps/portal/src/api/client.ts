import type { PersonaResponse, ServiceDetail, ServiceSummary } from "./types";

/** An HTTP error from the backend. FormCMS errors look like { title, code }. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const res = await fetch(path, {
    credentials: "same-origin",
    ...init,
    headers: { Accept: "application/json", ...(init?.body ? { "Content-Type": "application/json" } : {}) },
  });
  if (!res.ok) {
    let message = res.statusText;
    try {
      const body = await res.json();
      message = body?.title ?? message;
    } catch {
      // response had no JSON body
    }
    throw new ApiError(res.status, message);
  }
  return res;
}

/** Parses JSON, treating an empty body as null (FormCMS single queries return empty when nothing matches). */
async function jsonOrNull<T>(res: Response): Promise<T | null> {
  const text = await res.text();
  return text && text !== "null" ? (JSON.parse(text) as T) : null;
}

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
};
