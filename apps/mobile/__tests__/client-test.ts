import { api, ApiError, setSessionToken } from "../src/api/client";
import { apiBaseUrl } from "../src/config";
import { fakeBackend } from "../test/backend";
import { convertFormDataAsync } from "expo/src/winter/fetch/convertFormData";

describe("API configuration", () => {
  const original = process.env.EXPO_PUBLIC_API_BASE_URL;
  afterEach(() => {
    process.env.EXPO_PUBLIC_API_BASE_URL = original;
  });

  it("uses EXPO_PUBLIC_API_BASE_URL and refuses a missing or malformed address", async () => {
    process.env.EXPO_PUBLIC_API_BASE_URL = "http://192.168.1.20:5100/";
    expect(apiBaseUrl()).toBe("http://192.168.1.20:5100");
    for (const bad of ["", "   ", "192.168.1.20:5100", "ftp://x"]) {
      process.env.EXPO_PUBLIC_API_BASE_URL = bad;
      expect(apiBaseUrl()).toBeNull();
    }
    await expect(api.listServices()).rejects.toMatchObject({ status: 0, message: "apiNotConfigured" });
  });
});

describe("API client", () => {
  afterEach(() => setSessionToken(null));

  it("reports an unreachable backend as offline (status 0)", async () => {
    fakeBackend({ "GET /api/modon/requests": "offline" });
    const error = await api.listMyRequests().catch((e) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).offline).toBe(true);
  });

  it("passes the backend's problem title and field errors through", async () => {
    fakeBackend({ "PUT /api/modon/requests/7/excavation": { status: 400, body: { title: "One or more validation errors occurred.", errors: { endDate: ["endDateMustBeAfterIssueDate"] } } } });
    const error = (await api.saveExcavation(7, {} as never).catch((e) => e)) as ApiError;
    expect(error.status).toBe(400);
    expect(error.errors).toEqual({ endDate: ["endDateMustBeAfterIssueDate"] });
  });

  it("uploads as multipart with the content type of the file's extension (the backend checks it)", async () => {
    setSessionToken("mdn_x");
    const { calls } = fakeBackend({
      "POST /api/modon/requests/7/attachments/workRoutePlan": { status: 200, body: { slotCode: "workRoutePlan", fileName: "plan.PDF", contentType: "application/pdf", sizeBytes: 9, uploadedAt: "" } },
    });

    const append = jest.spyOn(FormData.prototype, "append");

    await api.uploadAttachment(7, "workRoutePlan", { uri: "file:///x/plan.PDF", name: "plan.PDF", size: 9, mimeType: "application/octet-stream" });

    // React Native's FormData sends { uri, name, type } as a file part; Expo's fetch reads the content through bytes().
    expect(append).toHaveBeenCalledWith("file", expect.objectContaining({ uri: "file:///x/plan.PDF", name: "plan.PDF", type: "application/pdf" }));
    const part = append.mock.calls[0][1] as unknown as { bytes: () => Promise<Uint8Array> };
    await expect(part.bytes()).resolves.toEqual(new TextEncoder().encode("bytes of file:///x/plan.PDF"));
    expect(calls[0].body).toBeInstanceOf(FormData);
    append.mockRestore();
    expect(calls[0].headers).toMatchObject({ Authorization: "Bearer mdn_x", "X-Modon-Client": "mobile" });
  });

  it("builds an upload body that Expo's global fetch can encode (it rejects plain { uri } parts)", async () => {
    // On the device FormData is React Native's, patched by Expo; Jest's global FormData is Node's.
    const RNFormData = require("react-native/Libraries/Network/FormData").default;
    const original = globalThis.FormData;
    globalThis.FormData = RNFormData;
    try {
      const { calls } = fakeBackend({ "POST /api/modon/requests/7/attachments/workLocationPhoto": { status: 200, body: {} } });
      await api.uploadAttachment(7, "workLocationPhoto", { uri: "file:///x/IMG_1.JPG", name: "IMG_1.JPG", size: 9, mimeType: null });

      const form = calls[0].body as unknown as { _parts: unknown[]; entries?: () => unknown };
      form.entries = () => form._parts; // what Expo's FormData patch yields on the device
      const { body } = await convertFormDataAsync(form as unknown as FormData);
      const text = new TextDecoder().decode(body);
      expect(text).toContain('content-disposition: form-data; name="file"; filename="IMG_1.JPG"');
      expect(text).toContain("content-type: image/jpeg");
      expect(text).toContain("bytes of file:///x/IMG_1.JPG");
    } finally {
      globalThis.FormData = original;
    }
  });
});
