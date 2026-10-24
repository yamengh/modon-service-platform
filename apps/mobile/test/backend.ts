import * as SecureStore from "expo-secure-store";
import type { PersonaResponse, RequestDetail } from "../src/api/types";

/** A recorded HTTP call made by the app. */
export interface Call {
  method: string;
  path: string;
  headers: Record<string, string>;
  body: unknown;
}

type Reply = { status: number; body?: unknown } | "offline";
type Handler = (call: Call) => Reply | undefined;

/**
 * A fake Modon backend for the app's fetch calls: routes are "METHOD /path" (query string included) or a function.
 * Unknown routes answer 404 so a missing stub is visible in the test.
 */
export function fakeBackend(routes: Record<string, Reply | Handler>) {
  const calls: Call[] = [];
  const fetchMock = jest.fn(async (url: string, init: RequestInit = {}) => {
    const path = url.replace("http://api.test:5100", "");
    const method = (init.method ?? "GET").toUpperCase();
    const body = typeof init.body === "string" ? JSON.parse(init.body) : init.body;
    const call: Call = { method, path, headers: (init.headers ?? {}) as Record<string, string>, body };
    calls.push(call);
    const route = routes[`${method} ${path}`];
    const reply: Reply = (typeof route === "function" ? route(call) : route) ?? { status: 404, body: { title: "Not Found" } };
    if (reply === "offline") throw new TypeError("Network request failed");
    const text = reply.body === undefined ? "" : JSON.stringify(reply.body);
    return {
      ok: reply.status >= 200 && reply.status < 300,
      status: reply.status,
      statusText: String(reply.status),
      json: async () => JSON.parse(text),
      text: async () => text,
    };
  });
  globalThis.fetch = fetchMock as unknown as typeof fetch;
  return { calls, fetchMock };
}

export const TOKEN = "mdn_test-token-0123456789";

export async function storeToken(token = TOKEN) {
  await SecureStore.setItemAsync("modon.session.token", token);
}

export async function storedToken() {
  return SecureStore.getItemAsync("modon.session.token");
}

export async function clearStore() {
  await SecureStore.deleteItemAsync("modon.session.token");
  await SecureStore.deleteItemAsync("modon.lang");
}

export const contractor: PersonaResponse = {
  id: "u3", email: "contractor@modon.local", name: "contractor@modon.local", roles: ["contractor"], personas: ["contractor"],
  canUsePortal: true,
};

export const investor: PersonaResponse = { ...contractor, id: "u1", email: "investor@modon.local", name: "investor@modon.local", roles: ["investor"], personas: ["investor"] };

export const consultant: PersonaResponse = { ...contractor, id: "u4", email: "consultant@modon.local", name: "consultant@modon.local", roles: ["consultant"], personas: ["consultant"] };

/** Routes every signed-in screen touches (tab badge, empty lists). */
export function signedInRoutes(user: PersonaResponse) {
  return {
    "GET /api/modon/me/personas": { status: 200, body: user },
    "GET /api/modon/notifications/unread-count": { status: 200, body: { unreadCount: 0 } },
    "GET /api/queries/modonServices": { status: 200, body: [] },
  } as const;
}

const pdf = [".jpeg", ".pdf", ".jpg", ".png"];
const slot = (code: string, labelAr: string, labelEn: string, extensions = pdf, maxSizeKb = 20480, requiredOnSubmit = true) =>
  ({ code, labelAr, labelEn, requiredOnSubmit, maxSizeKb, extensions });

export const excavationSlots = [
  slot("workLocationPhoto", "صورة لموقع العمل", "Work location photo", [".jpeg", ".jpg", ".png"]),
  slot("workRoutePlan", "مخطط مسار العمل", "Work route plan"),
  slot("excavationCrossSection", "مقطع عرضي للحفر", "Excavation cross-section"),
  slot("projectSchedule", "الجدول الزمني للمشروع", "Project schedule"),
  slot("trafficSafetyPlan", "خطة سلامة السير", "Traffic safety plan"),
  slot("ownerEntityLetter", "خطاب الجهة المالكة", "Owner entity letter"),
];
export const letterSlots = [
  slot("secCoordinationLetter", "خطاب تنسيق الشركة السعودية للكهرباء", "Saudi Electricity Company coordination letter"),
  slot("nationalGridCoordinationLetter", "خطاب تنسيق الشركة الوطنية لنقل الكهرباء", "National Grid coordination letter"),
  slot("eastElectricityOfficeCoordinationLetter", "خطاب تنسيق مكتب خدمة كهرباء شرق", "East electricity service office coordination letter"),
];

export const emptyExcavation = {
  establishmentId: null, requestTypeCode: null, contractTypeCode: null, industrialCityCode: null, permitTypeCode: null, workDescription: null,
  issueDate: null, endDate: null, excavationMethodCode: null, workLocation: null, lengthM: null, widthM: null, depthM: null,
  totalPathLengthM: null, projectManagerName: null, projectManagerPhone: null, projectManagerEmail: null, supervisingEntity: null,
};

export function excavationRequest(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return {
    id: 7, requestNumber: "EXC-000007-15-10-26", serviceCode: "excavation-permit", serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit", status: "draft", canEdit: true, createdAt: "2026-10-15T10:00:00Z",
    updatedAt: "2026-10-15T10:00:00Z", submittedAt: null, excavation: emptyExcavation, attachmentSlots: excavationSlots,
    attachments: [], history: [{ fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T10:00:00Z", comment: null }],
    coordinationLetterSlots: letterSlots, canUploadCoordinationLetters: false, currentTask: null, building: null,
    ...overrides,
  };
}
