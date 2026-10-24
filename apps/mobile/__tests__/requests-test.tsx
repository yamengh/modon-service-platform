import { fireEvent, within } from "@testing-library/react-native";
import * as DocumentPicker from "expo-document-picker";
import { renderRouter, screen } from "expo-router/testing-library";
import {
  clearStore, contractor, excavationRequest, fakeBackend, investor, signedInRoutes, storeToken,
} from "../test/backend";

const APP = "./src/app";

beforeEach(async () => {
  await clearStore();
  await storeToken();
});

const services = [
  { id: 1, code: "building-licence", nameAr: "رخصة البناء وتقارير البناء", nameEn: "Building Licence & Construction Reports", summaryAr: "ملخص", summaryEn: "Summary", beneficiary: "investor", sortOrder: 1 },
  { id: 2, code: "excavation-permit", nameAr: "تصريح أعمال الحفر", nameEn: "Excavation Works Permit", summaryAr: "ملخص", summaryEn: "Summary", beneficiary: "contractor", sortOrder: 2 },
];

describe("Catalogue and starting a service", () => {
  it("lists both Modon services and lets a contractor start the excavation permit", async () => {
    const { calls } = fakeBackend({
      ...signedInRoutes(contractor),
      "GET /api/queries/modonServices": { status: 200, body: services },
      "GET /api/queries/modonService/single?code=excavation-permit": { status: 200, body: { ...services[1], stepsAr: "الدخول\nالتقديم" } },
      "POST /api/modon/requests": { status: 201, body: excavationRequest() },
      "GET /api/modon/requests/7": { status: 200, body: excavationRequest() },
      "GET /api/modon/me/establishments": { status: 200, body: [] },
      ...Object.fromEntries(["excavationRequestType", "excavationContractType", "industrialCity", "excavationPermitType", "excavationMethod"]
        .map((c) => [`GET /api/queries/modonLookups?category=${c}`, { status: 200, body: [] }])),
    });
    await renderRouter(APP, { initialUrl: "/" });

    expect(await screen.findByText("رخصة البناء وتقارير البناء")).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole("button", { name: "تصريح أعمال الحفر" }));
    await fireEvent.press(await screen.findByRole("button", { name: "بدء الخدمة" }));

    expect(await screen.findByRole("header", { name: "تصريح أعمال الحفر" })).toBeOnTheScreen();
    expect(calls.find((c) => c.method === "POST" && c.path === "/api/modon/requests")!.body).toEqual({ serviceCode: "excavation-permit" });
  });

  it("does not offer a contractor's service to an investor", async () => {
    fakeBackend({
      ...signedInRoutes(investor),
      "GET /api/queries/modonService/single?code=excavation-permit": { status: 200, body: services[1] },
    });
    await renderRouter(APP, { initialUrl: "/services/excavation-permit" });

    expect(await screen.findByText("هذه الخدمة مخصصة لفئة أخرى من المستخدمين.")).toBeOnTheScreen();
    expect(screen.getByRole("button", { name: "بدء الخدمة" })).toBeDisabled();
  });
});

describe("My requests and details", () => {
  it("lists my requests with number, service, status and dates; shows an empty state", async () => {
    fakeBackend({
      ...signedInRoutes(contractor),
      "GET /api/modon/requests": {
        status: 200,
        body: [{ id: 7, requestNumber: "EXC-000007-15-10-26", serviceCode: "excavation-permit", serviceNameAr: "تصريح أعمال الحفر", serviceNameEn: "Excavation Works Permit",
          status: "returnedForCompletion", createdAt: "2026-10-15T10:00:00Z", updatedAt: "2026-10-16T10:00:00Z", submittedAt: "2026-10-15T11:00:00Z" }],
      },
    });
    await renderRouter(APP, { initialUrl: "/requests" });

    const card = await screen.findByRole("button", { name: /EXC-000007-15-10-26/ });
    expect(within(card).getByText("إكمال النواقص")).toBeOnTheScreen();
    expect(within(card).getByText(/تاريخ التقديم: 15\/10\/2026/)).toBeOnTheScreen();
  });

  it("shows the history with the reviewer's comment and the task to complete", async () => {
    fakeBackend({
      ...signedInRoutes(contractor),
      "GET /api/modon/requests/7": {
        status: 200,
        body: excavationRequest({
          status: "returnedForCompletion",
          submittedAt: "2026-10-15T11:00:00Z",
          history: [
            { fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T10:00:00Z", comment: null },
            { fromStatus: "draft", toStatus: "inProgress", action: "submit", occurredAt: "2026-10-15T11:00:00Z", comment: null },
            { fromStatus: "inProgress", toStatus: "returnedForCompletion", action: "returnForCompletion", occurredAt: "2026-10-16T09:00:00Z", comment: "صحح موقع العمل" },
          ],
          currentTask: { kind: "completeReturnedRequest", instructions: "صحح موقع العمل", assignedAt: "2026-10-16T09:00:00Z" },
        }),
      },
    });
    await renderRouter(APP, { initialUrl: "/requests/7" });

    expect(await screen.findByText("إعادة الطلب لإكمال النواقص")).toBeOnTheScreen();
    expect(screen.getAllByText(/صحح موقع العمل/).length).toBeGreaterThanOrEqual(2); // task + history
    expect(screen.getByRole("button", { name: "إكمال النواقص وإعادة التقديم" })).toBeOnTheScreen();
  });
});

describe("Excavation form", () => {
  const routes = () => ({
    ...signedInRoutes(contractor),
    "GET /api/modon/requests/7": { status: 200, body: excavationRequest() },
    "GET /api/modon/me/establishments": { status: 200, body: [{ id: 1, crNumber: "7012345678", nameAr: "مؤسسة البناء", nameEn: "Building Est." }] },
    "GET /api/queries/modonLookups?category=excavationRequestType": { status: 200, body: [{ code: "new", nameAr: "طلب جديد", nameEn: "New request" }] },
    "GET /api/queries/modonLookups?category=excavationContractType": { status: 200, body: [] },
    "GET /api/queries/modonLookups?category=industrialCity": { status: 200, body: [] },
    "GET /api/queries/modonLookups?category=excavationPermitType": { status: 200, body: [] },
    "GET /api/queries/modonLookups?category=excavationMethod": { status: 200, body: [] },
  });

  it("checks required fields, dates and the six attachments before submitting", async () => {
    const { calls } = fakeBackend(routes());
    await renderRouter(APP, { initialUrl: "/requests/7/excavation" });

    await fireEvent.changeText(await screen.findByLabelText("تاريخ الإصدار"), "2026-02-30");
    await fireEvent.press(screen.getByRole("button", { name: "تقديم الطلب" }));

    expect(screen.getByText(/تعذر تقديم الطلب/)).toBeOnTheScreen();
    expect(screen.getByText("أدخل تاريخاً صحيحاً بصيغة YYYY-MM-DD")).toBeOnTheScreen();
    expect(screen.getAllByText("هذا الحقل مطلوب").length).toBeGreaterThanOrEqual(10);
    expect(calls.some((c) => c.method === "POST" && c.path.endsWith("/submit"))).toBe(false);
  });

  it("saves a draft with the typed values", async () => {
    const { calls } = fakeBackend({ ...routes(), "PUT /api/modon/requests/7/excavation": { status: 200, body: excavationRequest() } });
    await renderRouter(APP, { initialUrl: "/requests/7/excavation" });

    await fireEvent.press(await screen.findByRole("radio", { name: "مؤسسة البناء – 7012345678" }));
    await fireEvent.changeText(screen.getByLabelText("موقع العمل – رقم الشارع"), "شارع 12");
    await fireEvent.press(screen.getByRole("button", { name: "حفظ كمسودة" }));

    expect(await screen.findByText("تم حفظ المسودة بنجاح.")).toBeOnTheScreen();
    expect(calls.find((c) => c.method === "PUT")!.body).toMatchObject({ establishmentId: 1, workLocation: "شارع 12" });
  });

  it("uploads a picked file through the protected endpoint, and offers a retry after a failure", async () => {
    let fail = true;
    const { calls } = fakeBackend({
      ...routes(),
      "POST /api/modon/requests/7/attachments/workRoutePlan": () =>
        fail ? "offline" : { status: 200, body: { slotCode: "workRoutePlan", fileName: "route.pdf", contentType: "application/pdf", sizeBytes: 2048, uploadedAt: "2026-10-15T10:00:00Z" } },
    });
    jest.mocked(DocumentPicker.getDocumentAsync).mockResolvedValue({
      canceled: false,
      assets: [{ uri: "file:///cache/route.pdf", name: "route.pdf", size: 2048, mimeType: "application/pdf", lastModified: 0 }],
    });
    await renderRouter(APP, { initialUrl: "/requests/7/excavation" });

    const pickButtons = await screen.findAllByRole("button", { name: "اختيار ملف" });
    await fireEvent.press(pickButtons[1]); // work route plan
    expect(await screen.findByText("تعذر الوصول إلى الخادم. تحقق من الاتصال وحاول مرة أخرى.")).toBeOnTheScreen();

    fail = false;
    await fireEvent.press(screen.getByRole("button", { name: "إعادة رفع الملف" }));

    expect(await screen.findByText(/route\.pdf/)).toBeOnTheScreen();
    const upload = calls.filter((c) => c.path.endsWith("/attachments/workRoutePlan")).at(-1)!;
    expect(upload.headers.Authorization).toMatch(/^Bearer mdn_/);
    expect(upload.body).toBeInstanceOf(FormData);
  });

  it("rejects a file type the slot does not allow without uploading it", async () => {
    const { calls } = fakeBackend(routes());
    jest.mocked(DocumentPicker.getDocumentAsync).mockResolvedValue({
      canceled: false,
      assets: [{ uri: "file:///cache/photo.pdf", name: "photo.pdf", size: 10, mimeType: "application/pdf", lastModified: 0 }],
    });
    await renderRouter(APP, { initialUrl: "/requests/7/excavation" });

    await fireEvent.press((await screen.findAllByRole("button", { name: "اختيار ملف" }))[0]); // work location photo: images only

    expect(await screen.findByText("نوع الملف غير مسموح لهذا المرفق")).toBeOnTheScreen();
    expect(calls.some((c) => c.path.includes("/attachments/"))).toBe(false);
  });
});
