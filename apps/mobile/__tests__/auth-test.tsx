import { fireEvent } from "@testing-library/react-native";
import { renderRouter, screen } from "expo-router/testing-library";
import { TOKEN, clearStore, contractor, fakeBackend, signedInRoutes, storeToken, storedToken } from "../test/backend";

const APP = "./src/app";

beforeEach(async () => {
  await clearStore();
});

async function signIn(email: string, password: string) {
  await fireEvent.changeText(await screen.findByLabelText("البريد الإلكتروني"), email);
  await fireEvent.changeText(screen.getByLabelText("كلمة المرور"), password);
  await fireEvent.press(screen.getByRole("button", { name: "تسجيل الدخول" }));
}

describe("Mobile sign-in", () => {
  it("signs in with a valid account, keeps the token in secure storage and sends it as a bearer token", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(contractor),
      "POST /api/modon/mobile/session": { status: 200, body: { token: TOKEN, expiresAt: "2026-11-20T00:00:00Z", user: contractor } },
    });
    await renderRouter(APP, { initialUrl: "/login" });

    await signIn("contractor@modon.local", "Modon@Dev1");

    expect(await screen.findByRole("header", { name: "خدمات مدن" })).toBeOnTheScreen();
    expect(await storedToken()).toBe(TOKEN);
    const login = calls.find((c) => c.path === "/api/modon/mobile/session")!;
    expect(login.body).toEqual({ email: "contractor@modon.local", password: "Modon@Dev1", deviceName: expect.any(String) });
    expect(login.headers["X-Modon-Client"]).toBe("mobile");
    const later = calls.find((c) => c.path === "/api/queries/modonServices")!;
    expect(later.headers.Authorization).toBe(`Bearer ${TOKEN}`);
  });

  it("refuses wrong credentials without storing anything", async () => {
    fakeBackend({ "POST /api/modon/mobile/session": { status: 401, body: { title: "invalidCredentials" } } });
    await renderRouter(APP, { initialUrl: "/login" });

    await signIn("contractor@modon.local", "wrong");

    expect(await screen.findByText("تعذر تسجيل الدخول. تحقق من البريد الإلكتروني وكلمة المرور.")).toBeOnTheScreen();
    expect(await storedToken()).toBeNull();
  });

  it("checks the form before calling the backend", async () => {
    const { calls } = fakeBackend({});
    await renderRouter(APP, { initialUrl: "/login" });

    await fireEvent.press(await screen.findByRole("button", { name: "تسجيل الدخول" }));

    expect(screen.getByText("البريد الإلكتروني مطلوب")).toBeOnTheScreen();
    expect(screen.getByText("كلمة المرور مطلوبة")).toBeOnTheScreen();
    expect(calls).toHaveLength(0);
  });

  it("explains the role restriction when the backend refuses a reviewer (403)", async () => {
    fakeBackend({ "POST /api/modon/mobile/session": { status: 403, body: { title: "roleNotAllowed" } } });
    await renderRouter(APP, { initialUrl: "/login" });

    await signIn("reviewer@modon.local", "Modon@Dev1");

    expect(await screen.findByText(/هذا التطبيق للمستثمرين والمقاولين والمكاتب الاستشارية/)).toBeOnTheScreen();
    expect(await storedToken()).toBeNull();
  });

  it("shows a network error when the backend cannot be reached", async () => {
    fakeBackend({ "POST /api/modon/mobile/session": "offline" });
    await renderRouter(APP, { initialUrl: "/login" });

    await signIn("contractor@modon.local", "Modon@Dev1");

    expect(await screen.findByText("تعذر الوصول إلى الخادم. تحقق من الاتصال وحاول مرة أخرى.")).toBeOnTheScreen();
  });
});

describe("Session restoration", () => {
  it("restores a stored session after an app restart without asking for the password", async () => {
    await storeToken();
    const { calls } = fakeBackend(signedInRoutes(contractor));

    await renderRouter(APP, { initialUrl: "/" });

    expect(await screen.findByRole("header", { name: "خدمات مدن" })).toBeOnTheScreen();
    expect(calls[0]).toMatchObject({ method: "GET", path: "/api/modon/me/personas", headers: { Authorization: `Bearer ${TOKEN}` } });
  });

  it("signs out with a clear message when the stored session has expired (401)", async () => {
    await storeToken();
    fakeBackend({ "GET /api/modon/me/personas": { status: 401 } });

    await renderRouter(APP, { initialUrl: "/" });

    expect(await screen.findByText("انتهت جلستك. سجّل الدخول مرة أخرى.")).toBeOnTheScreen();
    expect(await storedToken()).toBeNull();
  });

  it("keeps the session and offers a retry when offline", async () => {
    await storeToken();
    fakeBackend({ "GET /api/modon/me/personas": "offline" });

    await renderRouter(APP, { initialUrl: "/" });

    expect(await screen.findByText("لا يوجد اتصال بالخادم")).toBeOnTheScreen();
    expect(screen.getByRole("button", { name: "إعادة المحاولة" })).toBeOnTheScreen();
    expect(await storedToken()).toBe(TOKEN);
  });

  it("returns to sign-in when the backend rejects the token during use", async () => {
    await storeToken();
    fakeBackend({ ...signedInRoutes(contractor), "GET /api/modon/requests": { status: 401 } });

    await renderRouter(APP, { initialUrl: "/requests" });

    expect(await screen.findByText("انتهت جلستك. سجّل الدخول مرة أخرى.")).toBeOnTheScreen();
    expect(await storedToken()).toBeNull();
  });
});

describe("Logout", () => {
  it("revokes the token on the server and forgets it on the device", async () => {
    await storeToken();
    const { calls } = fakeBackend({ ...signedInRoutes(contractor), "DELETE /api/modon/mobile/session": { status: 204 } });
    await renderRouter(APP, { initialUrl: "/more" });

    await fireEvent.press(await screen.findByRole("button", { name: "تسجيل الخروج" }));

    expect(await screen.findByText("تم تسجيل الخروج.")).toBeOnTheScreen();
    expect(calls.find((c) => c.method === "DELETE")!.headers.Authorization).toBe(`Bearer ${TOKEN}`);
    expect(await storedToken()).toBeNull();
  });

  it("still signs out locally when offline", async () => {
    await storeToken();
    fakeBackend({ ...signedInRoutes(contractor), "DELETE /api/modon/mobile/session": "offline" });
    await renderRouter(APP, { initialUrl: "/more" });

    await fireEvent.press(await screen.findByRole("button", { name: "تسجيل الخروج" }));

    expect(await screen.findByRole("button", { name: "تسجيل الدخول" })).toBeOnTheScreen();
    expect(await storedToken()).toBeNull();
  });
});
