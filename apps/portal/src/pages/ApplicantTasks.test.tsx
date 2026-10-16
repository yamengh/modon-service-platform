import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import type { AttachmentInfo, HistoryItem, RequestDetail, RequestSummary, TaskItem } from "../api/types";
import { allAttachments, completeExcavation, coordinationLetterSlots, excavationRequest, renderApp } from "../test/render";
import { fakeFile, useExcavationBackend } from "../test/excavationMocks";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const user = () => userEvent.setup({ applyAccept: false });
const slotOf = (code: string) => document.querySelector(`[data-slot="${code}"]`) as HTMLElement;
const historyBox = () => screen.getByRole("heading", { name: "سجل حالة الطلب" }).parentElement!;

const submittedHistory: HistoryItem[] = [
  { fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T10:00:00Z", comment: null },
  { fromStatus: "draft", toStatus: "inProgress", action: "submit", occurredAt: "2026-10-15T11:00:00Z", comment: null },
];

function returnedRequest(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return excavationRequest({
    status: "returnedForCompletion",
    canEdit: true,
    excavation: completeExcavation,
    attachments: allAttachments,
    submittedAt: "2026-10-15T11:00:00Z",
    history: [
      ...submittedHistory,
      { fromStatus: "inProgress", toStatus: "returnedForCompletion", action: "returnForCompletion", occurredAt: "2026-10-16T09:00:00Z", comment: "صحح موقع العمل" },
    ],
    currentTask: { kind: "completeReturnedRequest", instructions: "صحح موقع العمل", assignedAt: "2026-10-16T09:00:00Z" },
    ...overrides,
  });
}

function awaitingLetters(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return excavationRequest({
    status: "awaitingCoordinationLetters",
    canEdit: false,
    canUploadCoordinationLetters: true,
    excavation: completeExcavation,
    attachments: allAttachments,
    submittedAt: "2026-10-15T11:00:00Z",
    history: [
      ...submittedHistory,
      { fromStatus: "inProgress", toStatus: "awaitingCoordinationLetters", action: "requestCoordinationLetters", occurredAt: "2026-10-16T09:00:00Z", comment: "الخطابات الثلاثة مطلوبة" },
    ],
    currentTask: { kind: "uploadCoordinationLetters", instructions: "الخطابات الثلاثة مطلوبة", assignedAt: "2026-10-16T09:00:00Z" },
    ...overrides,
  });
}

const letter = (slotCode: string, fileName = `${slotCode}.pdf`): AttachmentInfo => ({
  slotCode, fileName, contentType: "application/pdf", sizeBytes: 2048, uploadedAt: "2026-10-16T10:00:00Z",
});
const allLetters = coordinationLetterSlots.map((s) => letter(s.code));

const tasks: TaskItem[] = [
  {
    requestId: 7, requestNumber: "EXC-000007-15-10-26", serviceCode: "excavation-permit", serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit", status: "returnedForCompletion", kind: "completeReturnedRequest",
    instructions: "صحح موقع العمل", assignedAt: "2026-10-16T09:00:00Z", submittedAt: "2026-10-15T11:00:00Z",
  },
  {
    requestId: 9, requestNumber: "EXC-000009-15-10-26", serviceCode: "excavation-permit", serviceNameAr: "تصريح أعمال الحفر",
    serviceNameEn: "Excavation Works Permit", status: "awaitingCoordinationLetters", kind: "uploadCoordinationLetters",
    instructions: null, assignedAt: "2026-10-14T09:00:00Z", submittedAt: "2026-10-13T11:00:00Z",
  },
];

describe("Task inbox", () => {
  beforeEach(() => useExcavationBackend());

  it("lists the requests waiting for me with number, service, status, instructions, date and an action link", async () => {
    vi.mocked(api.listTasks).mockResolvedValue(tasks);

    renderApp("/tasks");

    const rows = (await screen.findAllByRole("row")).slice(1);
    expect(rows).toHaveLength(2);
    const first = within(rows[0]);
    expect(first.getByRole("link", { name: "EXC-000007-15-10-26" })).toHaveAttribute("href", "/requests/7");
    expect(first.getByText("تصريح أعمال الحفر")).toBeInTheDocument();
    expect(first.getByText("إكمال النواقص")).toBeInTheDocument();
    expect(first.getByText("صحح موقع العمل")).toBeInTheDocument();
    expect(first.getByText("16/10/2026")).toBeInTheDocument();
    expect(first.getByRole("link", { name: "إكمال النواقص وإعادة التقديم" })).toHaveAttribute("href", "/requests/7/edit");
    const second = within(rows[1]);
    expect(second.getByText("بانتظار خطابات التنسيق")).toBeInTheDocument();
    expect(second.getByText("لا توجد ملاحظات.")).toBeInTheDocument();
    expect(second.getByRole("link", { name: "رفع خطابات التنسيق" })).toHaveAttribute("href", "/requests/9/coordination-letters");
    expect(screen.getByRole("link", { name: "المهام" })).toHaveAttribute("href", "/tasks");
  });

  it("shows the inbox in English", async () => {
    vi.mocked(api.listTasks).mockResolvedValue(tasks);

    renderApp("/tasks", "en");

    expect(await screen.findByRole("heading", { name: "Tasks" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "Complete and resubmit" })).toBeInTheDocument();
    expect(screen.getByText("Awaiting coordination letters")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("ltr");
  });

  it("shows empty and error states", async () => {
    vi.mocked(api.listTasks).mockResolvedValueOnce([]);
    const { unmount } = renderApp("/tasks");
    expect(await screen.findByText("لا توجد مهام بانتظارك حالياً.")).toBeInTheDocument();
    unmount();

    vi.mocked(api.listTasks).mockRejectedValueOnce(new Error("network"));
    renderApp("/tasks");
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل البيانات");
  });

  it("links returned and letter requests from My requests to the right action", async () => {
    const summary = (id: number, status: RequestSummary["status"]): RequestSummary => ({
      id, requestNumber: `EXC-00000${id}-15-10-26`, serviceCode: "excavation-permit", serviceNameAr: "تصريح أعمال الحفر",
      serviceNameEn: "Excavation Works Permit", status, createdAt: "2026-10-15T10:00:00Z", updatedAt: "2026-10-16T09:00:00Z",
      submittedAt: "2026-10-15T11:00:00Z",
    });
    vi.mocked(api.listMyRequests).mockResolvedValue([summary(7, "returnedForCompletion"), summary(9, "awaitingCoordinationLetters"), summary(5, "inProgress")]);

    renderApp("/requests");

    const rows = (await screen.findAllByRole("row")).slice(1);
    expect(within(rows[0]).getByRole("link", { name: "إكمال النواقص وإعادة التقديم" })).toHaveAttribute("href", "/requests/7/edit");
    expect(within(rows[1]).getByRole("link", { name: "رفع خطابات التنسيق" })).toHaveAttribute("href", "/requests/9/coordination-letters");
    expect(within(rows[2]).getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/requests/5");
  });
});

describe("Returned request: edit and resubmit", () => {
  beforeEach(() => useExcavationBackend());

  async function openReturned(request = returnedRequest()) {
    vi.mocked(api.getRequest).mockResolvedValue(request);
    renderApp("/requests/7/edit");
    await screen.findByRole("button", { name: "حفظ التعديلات" });
  }

  it("shows the reviewer's instructions on the request and links to the form", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(returnedRequest());

    renderApp("/requests/7");

    expect(await screen.findByRole("heading", { name: "أعادت مدن هذا الطلب لإكمال النواقص" })).toBeInTheDocument();
    expect(screen.getAllByText("صحح موقع العمل").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "إكمال النواقص وإعادة التقديم" })).toHaveAttribute("href", "/requests/7/edit");
    expect(within(historyBox()).getByText("إعادة الطلب لإكمال النواقص")).toBeInTheDocument();
  });

  it("reopens the original application, keeps the establishment, and saves changes without resubmitting", async () => {
    await openReturned();
    vi.mocked(api.saveExcavation).mockResolvedValue(returnedRequest());
    const u = user();

    expect(screen.getByText("صحح موقع العمل")).toBeInTheDocument();
    expect(screen.getByLabelText(/السجل التجاري للمنشأة/)).toBeDisabled();
    expect(screen.getByText("لا يمكن تغيير المنشأة بعد تقديم الطلب.")).toBeInTheDocument();
    const location = screen.getByLabelText(/موقع العمل – رقم الشارع/);
    expect(location).toHaveValue("شارع 12");
    await u.clear(location);
    await u.type(location, "شارع 14");
    await u.click(screen.getByRole("button", { name: "حفظ التعديلات" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تم حفظ التعديلات. لم تتم إعادة تقديم الطلب بعد.");
    expect(api.saveExcavation).toHaveBeenCalledWith(7, { ...completeExcavation, workLocation: "شارع 14" });
    expect(api.submitRequest).not.toHaveBeenCalled();
  });

  it("blocks resubmission while a required attachment is missing", async () => {
    await openReturned(returnedRequest({ attachments: allAttachments.filter((a) => a.slotCode !== "trafficSafetyPlan") }));

    await user().click(screen.getByRole("button", { name: "إعادة تقديم الطلب" }));

    expect(screen.getAllByRole("alert")[0]).toHaveTextContent("تعذر إعادة تقديم الطلب");
    expect(within(slotOf("trafficSafetyPlan")).getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    expect(api.saveExcavation).not.toHaveBeenCalled();
    expect(api.submitRequest).not.toHaveBeenCalled();
  });

  it("shows the backend's validation errors when resubmission is refused", async () => {
    await openReturned();
    vi.mocked(api.saveExcavation).mockResolvedValue(returnedRequest());
    vi.mocked(api.submitRequest).mockRejectedValue(new ApiError(400, "", { establishmentId: ["cannotChange"] }));

    await user().click(screen.getByRole("button", { name: "إعادة تقديم الطلب" }));

    expect(await screen.findByText("لا يمكن تغيير هذه القيمة بعد تقديم الطلب")).toBeInTheDocument();
    expect(screen.getAllByRole("alert")[0]).toHaveTextContent("تعذر إعادة تقديم الطلب");
  });

  it("resubmits the same request and shows it back in progress with the full history", async () => {
    await openReturned();
    const resubmitted = returnedRequest({
      status: "inProgress", canEdit: false, currentTask: null,
      history: [...returnedRequest().history, { fromStatus: "returnedForCompletion", toStatus: "inProgress", action: "resubmit", occurredAt: "2026-10-16T12:00:00Z", comment: null }],
    });
    vi.mocked(api.saveExcavation).mockResolvedValue(returnedRequest());
    vi.mocked(api.submitRequest).mockResolvedValue(resubmitted);
    vi.mocked(api.getRequest).mockResolvedValue(resubmitted);

    await user().click(screen.getByRole("button", { name: "إعادة تقديم الطلب" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تمت إعادة تقديم الطلب بنجاح");
    expect(api.submitRequest).toHaveBeenCalledWith(7);
    expect(screen.getByText("EXC-000007-15-10-26")).toBeInTheDocument();
    const history = within(historyBox());
    expect(history.getByText("إعادة الطلب لإكمال النواقص")).toBeInTheDocument();
    expect(history.getByText("إعادة تقديم الطلب")).toBeInTheDocument();
    expect(screen.getByText("تم تقديم هذا الطلب ولا يمكن تعديله.")).toBeInTheDocument();
  });
});

describe("Coordination letters", () => {
  beforeEach(() => useExcavationBackend());

  async function openLetters(request = awaitingLetters()) {
    vi.mocked(api.getRequest).mockResolvedValue(request);
    renderApp("/requests/7/coordination-letters");
    await screen.findByRole("button", { name: "إرسال خطابات التنسيق" });
  }

  it("shows the task on the request with a link to upload the letters", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(awaitingLetters());

    renderApp("/requests/7");

    expect(await screen.findByRole("heading", { name: "تطلب مدن خطابات التنسيق" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "رفع خطابات التنسيق" })).toHaveAttribute("href", "/requests/7/coordination-letters");
    expect(screen.getAllByText("بانتظار خطابات التنسيق").length).toBeGreaterThan(0);
    expect(within(historyBox()).getByText("طلب خطابات التنسيق من مدن")).toBeInTheDocument();
  });

  it("requires all three letters before sending", async () => {
    await openLetters(awaitingLetters({ attachments: [...allAttachments, letter("secCoordinationLetter")] }));

    await user().click(screen.getByRole("button", { name: "إرسال خطابات التنسيق" }));

    expect(screen.getAllByRole("alert")[0]).toHaveTextContent("تعذر إرسال الخطابات");
    expect(within(slotOf("secCoordinationLetter")).queryByText("هذا الحقل مطلوب")).not.toBeInTheDocument();
    expect(within(slotOf("nationalGridCoordinationLetter")).getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    expect(within(slotOf("eastElectricityOfficeCoordinationLetter")).getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    expect(api.submitCoordinationLetters).not.toHaveBeenCalled();
  });

  it("checks letter files before uploading them", async () => {
    await openLetters();
    const u = user();
    const input = screen.getByLabelText(/خطاب تنسيق الشركة السعودية للكهرباء/);

    await u.upload(input, fakeFile("letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
    expect(within(slotOf("secCoordinationLetter")).getByRole("alert")).toHaveTextContent("نوع الملف غير مسموح");
    await u.upload(input, fakeFile("big.pdf", "application/pdf", 20480 * 1024 + 1));
    expect(within(slotOf("secCoordinationLetter")).getByRole("alert")).toHaveTextContent("حجم الملف أكبر من الحد المسموح");
    expect(api.uploadAttachment).not.toHaveBeenCalled();

    vi.mocked(api.uploadAttachment).mockRejectedValue(new ApiError(400, "", { "attachments.secCoordinationLetter": ["fileContentMismatch"] }));
    await u.upload(input, fakeFile("letter.png", "image/png"));
    expect(await within(slotOf("secCoordinationLetter")).findByRole("alert")).toHaveTextContent("محتوى الملف لا يطابق امتداده");
  });

  it("uploads, replaces and removes letters, then sends them and shows the request back in progress", async () => {
    await openLetters(awaitingLetters({ attachments: [...allAttachments, letter("eastElectricityOfficeCoordinationLetter", "old-east.pdf")] }));
    vi.mocked(api.uploadAttachment).mockImplementation(async (_id: number, slot: string, file: File) => letter(slot, file.name));
    vi.mocked(api.deleteAttachment).mockResolvedValue();
    const u = user();

    await u.upload(screen.getByLabelText(/خطاب تنسيق الشركة السعودية للكهرباء/), fakeFile("sec.pdf", "application/pdf"));
    await u.upload(screen.getByLabelText(/خطاب تنسيق الشركة الوطنية لنقل الكهرباء/), fakeFile("grid.pdf", "application/pdf"));
    expect(await within(slotOf("nationalGridCoordinationLetter")).findByRole("link", { name: "grid.pdf" }))
      .toHaveAttribute("href", "/api/modon/requests/7/attachments/nationalGridCoordinationLetter");
    await u.click(within(slotOf("eastElectricityOfficeCoordinationLetter")).getByRole("button", { name: "حذف" }));
    expect(api.deleteAttachment).toHaveBeenCalledWith(7, "eastElectricityOfficeCoordinationLetter");
    await u.upload(screen.getByLabelText(/خطاب تنسيق مكتب خدمة كهرباء شرق/), fakeFile("east.jpg", "image/jpeg"));
    expect(await within(slotOf("eastElectricityOfficeCoordinationLetter")).findByRole("link", { name: "east.jpg" })).toBeInTheDocument();

    const sent = awaitingLetters({
      status: "inProgress", canUploadCoordinationLetters: false, currentTask: null,
      attachments: [...allAttachments, letter("secCoordinationLetter", "sec.pdf"), letter("nationalGridCoordinationLetter", "grid.pdf"), letter("eastElectricityOfficeCoordinationLetter", "east.jpg")],
      history: [...awaitingLetters().history, { fromStatus: "awaitingCoordinationLetters", toStatus: "inProgress", action: "submitCoordinationLetters", occurredAt: "2026-10-16T12:00:00Z", comment: null }],
    });
    vi.mocked(api.submitCoordinationLetters).mockResolvedValue(sent);
    vi.mocked(api.getRequest).mockResolvedValue(sent);
    await u.click(screen.getByRole("button", { name: "إرسال خطابات التنسيق" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تم إرسال خطابات التنسيق");
    expect(api.submitCoordinationLetters).toHaveBeenCalledWith(7);
    const letters = screen.getByRole("heading", { name: "خطابات التنسيق" }).parentElement!;
    expect(within(letters).getByRole("link", { name: "east.jpg" }))
      .toHaveAttribute("href", "/api/modon/requests/7/attachments/eastElectricityOfficeCoordinationLetter");
    expect(within(historyBox()).getByText("إرسال خطابات التنسيق")).toBeInTheDocument();
  });

  it("shows the request's current status when it changed before sending", async () => {
    await openLetters(awaitingLetters({ attachments: [...allAttachments, ...allLetters] }));
    vi.mocked(api.submitCoordinationLetters).mockRejectedValue(new ApiError(409, "requestNotEditable"));
    vi.mocked(api.getRequest).mockResolvedValue(awaitingLetters({ status: "inProgress", canUploadCoordinationLetters: false, currentTask: null }));

    await user().click(screen.getByRole("button", { name: "إرسال خطابات التنسيق" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تغيرت حالة الطلب");
  });

  it("opens the request details instead when letters are not being requested", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(excavationRequest({ status: "inProgress", canEdit: false, submittedAt: "2026-10-15T11:00:00Z" }));

    renderApp("/requests/7/coordination-letters");

    expect(await screen.findByText("تم تقديم هذا الطلب ولا يمكن تعديله.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "إرسال خطابات التنسيق" })).not.toBeInTheDocument();
  });
});
