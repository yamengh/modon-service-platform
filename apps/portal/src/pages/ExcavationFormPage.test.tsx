import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import { allAttachments, completeExcavation, excavationRequest, renderApp } from "../test/render";
import { fakeFile, useExcavationBackend } from "../test/excavationMocks";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const user = () => userEvent.setup({ applyAccept: false });

async function openForm(request = excavationRequest()) {
  vi.mocked(api.getRequest).mockResolvedValue(request);
  renderApp(`/requests/${request.id}/edit`);
  await screen.findByRole("button", { name: "حفظ كمسودة" });
}

const slotOf = (code: string) => document.querySelector(`[data-slot="${code}"]`) as HTMLElement;

describe("Excavation permit form", () => {
  beforeEach(() => {
    useExcavationBackend();
  });

  it("shows the manual's sections, required markers and dropdown options", async () => {
    await openForm();

    for (const section of ["تفاصيل الطلب", "تفاصيل التصريح", "تفاصيل الأبعاد", "تفاصيل مدير المشروع", "المرفقات"]) {
      expect(screen.getByRole("group", { name: section })).toBeInTheDocument();
    }
    const establishment = screen.getByLabelText(/السجل التجاري للمنشأة/);
    expect(within(establishment).getByRole("option", { name: /7012345678/ })).toBeInTheDocument();
    expect(within(screen.getByLabelText(/المدينة الصناعية/)).getAllByRole("option")).toHaveLength(3);
    // dimensions are optional: no required marker
    expect(screen.getByLabelText("أبعاد العمل – الطول (متر طولي)")).toBeInTheDocument();
    expect(screen.getByLabelText(/^\* تاريخ الإصدار/)).toBeInTheDocument();
    expect(screen.getByText(/EXC-000007-15-10-26/)).toBeInTheDocument();
  });

  it("saves a partially completed draft", async () => {
    await openForm();
    vi.mocked(api.saveExcavation).mockResolvedValue(excavationRequest());
    const u = user();

    await u.selectOptions(screen.getByLabelText(/المدينة الصناعية/), "dammam-2");
    await u.type(screen.getByLabelText(/وصف العمل/), "  حفر تجريبي ");
    await u.type(screen.getByLabelText(/أبعاد العمل – الطول/), "12.5");
    await u.click(screen.getByRole("button", { name: "حفظ كمسودة" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تم حفظ المسودة بنجاح.");
    expect(api.saveExcavation).toHaveBeenCalledWith(7, {
      ...excavationRequest().excavation,
      industrialCityCode: "dammam-2",
      workDescription: "حفر تجريبي",
      lengthM: 12.5,
    });
    expect(api.submitRequest).not.toHaveBeenCalled();
  });

  it("blocks submission and lists missing fields and attachments", async () => {
    await openForm();

    await user().click(screen.getByRole("button", { name: "تقديم الطلب" }));

    // 14 required fields + 6 required attachments
    expect(screen.getByText(/تعذر تقديم الطلب/)).toHaveTextContent("يوجد 20 من الحقول تحتاج إلى مراجعة.");
    expect(screen.getByLabelText(/^\* نوع الطلب/)).toHaveAttribute("aria-invalid", "true");
    expect(within(slotOf("ownerEntityLetter")).getByRole("alert")).toHaveTextContent("هذا الحقل مطلوب");
    expect(screen.getByLabelText(/أبعاد العمل – الطول/)).toHaveAttribute("aria-invalid", "false");
    expect(api.saveExcavation).not.toHaveBeenCalled();
    expect(api.submitRequest).not.toHaveBeenCalled();
  });

  it("requires the end date to be after the issue date, even in a draft", async () => {
    await openForm();
    const u = user();

    await u.type(screen.getByLabelText(/تاريخ الإصدار/), "2026-12-10");
    await u.type(screen.getByLabelText(/تاريخ نهاية التصريح/), "2026-12-01");
    await u.click(screen.getByRole("button", { name: "حفظ كمسودة" }));

    expect(screen.getByText("يجب أن يكون تاريخ نهاية التصريح بعد تاريخ الإصدار")).toBeInTheDocument();
    expect(api.saveExcavation).not.toHaveBeenCalled();
  });

  it("shows validation errors returned by the backend", async () => {
    await openForm();
    vi.mocked(api.saveExcavation).mockRejectedValue(
      new ApiError(400, "One or more validation errors occurred.", {
        establishmentId: ["notYourEstablishment"],
        permitTypeCode: ["unknownOption"],
      }),
    );

    await user().click(screen.getByRole("button", { name: "حفظ كمسودة" }));

    expect(await screen.findByText("هذه المنشأة غير مرتبطة بحسابك")).toBeInTheDocument();
    expect(screen.getByText("القيمة المختارة غير متاحة")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("تعذر حفظ المسودة");
  });

  it("checks files before uploading and shows the uploaded file", async () => {
    await openForm();
    vi.mocked(api.uploadAttachment).mockResolvedValue({
      slotCode: "workRoutePlan", fileName: "route.pdf", contentType: "application/pdf", sizeBytes: 1024, uploadedAt: "2026-10-15T10:00:00Z",
    });
    const u = user();
    const input = screen.getByLabelText(/مخطط مسار العمل/);

    await u.upload(input, fakeFile("big.pdf", "application/pdf", 20480 * 1024 + 1));
    expect(within(slotOf("workRoutePlan")).getByRole("alert")).toHaveTextContent("حجم الملف أكبر من الحد المسموح");

    await u.upload(input, fakeFile("route.gif", "image/gif"));
    expect(within(slotOf("workRoutePlan")).getByRole("alert")).toHaveTextContent("نوع الملف غير مسموح");
    expect(api.uploadAttachment).not.toHaveBeenCalled();

    const valid = fakeFile("route.pdf", "application/pdf");
    await u.upload(input, valid);
    expect(api.uploadAttachment).toHaveBeenCalledWith(7, "workRoutePlan", valid);
    expect(await within(slotOf("workRoutePlan")).findByRole("link", { name: "route.pdf" }))
      .toHaveAttribute("href", "/api/modon/requests/7/attachments/workRoutePlan");
    expect(within(slotOf("workRoutePlan")).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the backend's reason when it rejects a file", async () => {
    await openForm();
    vi.mocked(api.uploadAttachment).mockRejectedValue(
      new ApiError(400, "", { "attachments.workLocationPhoto": ["fileContentMismatch"] }),
    );

    await user().upload(screen.getByLabelText(/صورة لموقع العمل/), fakeFile("site.png", "image/png"));

    expect(await within(slotOf("workLocationPhoto")).findByRole("alert")).toHaveTextContent("محتوى الملف لا يطابق امتداده");
  });

  it("submits a complete request and opens its details with a success message", async () => {
    const draft = excavationRequest({ excavation: completeExcavation, attachments: allAttachments });
    await openForm(draft);
    const submitted = excavationRequest({
      excavation: completeExcavation, attachments: allAttachments, status: "inProgress", canEdit: false,
      submittedAt: "2026-10-15T11:00:00Z",
      history: [...draft.history, { fromStatus: "draft", toStatus: "inProgress", action: "submit", occurredAt: "2026-10-15T11:00:00Z", comment: null }],
    });
    vi.mocked(api.saveExcavation).mockResolvedValue(draft);
    vi.mocked(api.submitRequest).mockResolvedValue(submitted);
    vi.mocked(api.getRequest).mockResolvedValue(submitted);

    await user().click(screen.getByRole("button", { name: "تقديم الطلب" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تم تقديم الطلب بنجاح");
    expect(api.saveExcavation).toHaveBeenCalledWith(7, completeExcavation);
    expect(api.submitRequest).toHaveBeenCalledWith(7);
    expect(vi.mocked(api.saveExcavation).mock.invocationCallOrder[0])
      .toBeLessThan(vi.mocked(api.submitRequest).mock.invocationCallOrder[0]);
    expect(screen.getAllByText("قيد التنفيذ").length).toBeGreaterThan(0);
  });

  it("opens submitted requests read-only instead of the form", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(excavationRequest({ status: "inProgress", canEdit: false }));

    renderApp("/requests/7/edit");

    expect(await screen.findByText("تم تقديم هذا الطلب ولا يمكن تعديله.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "حفظ كمسودة" })).not.toBeInTheDocument();
  });
});
