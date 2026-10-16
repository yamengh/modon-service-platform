import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import { renderApp, reviewDetail, useReviewerBackend } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

async function open(detail = reviewDetail()) {
  vi.mocked(api.getRequest).mockResolvedValue(detail);
  renderApp(`/requests/${detail.id}`);
  await screen.findByRole("heading", { name: "تصريح أعمال الحفر" });
}

const historyBox = () => screen.getByRole("heading", { name: "سجل الحالة والتدقيق" }).parentElement!;

describe("Request details", () => {
  beforeEach(() => useReviewerBackend());

  it("shows the submitted form, applicant, attachments and history", async () => {
    await open();

    expect(screen.getByText("EXC-000011-15-10-26")).toBeInTheDocument();
    expect(screen.getAllByText("مؤسسة البناء المتقدم للمقاولات").length).toBeGreaterThan(0);
    expect(screen.getByText("7012345678")).toBeInTheDocument();
    expect(screen.getByText("+966500000001")).toBeInTheDocument();
    expect(await screen.findByText("حفر مكشوف")).toBeInTheDocument(); // lookup code shown by name
    expect(screen.getByText("01/11/2026")).toBeInTheDocument();
    expect(screen.getByText("120.5 م")).toBeInTheDocument();
    expect(screen.getByText("pm@example.com")).toBeInTheDocument();

    expect(screen.getByRole("link", { name: "route.pdf" })).toHaveAttribute("href", "/api/modon/review/requests/11/attachments/workRoutePlan");
    expect(screen.getByText("لم يرفق")).toBeInTheDocument();

    expect(within(historyBox()).getByText("تقديم الطلب")).toBeInTheDocument();
    expect(within(historyBox()).getAllByText(/مقدم الطلب/)).toHaveLength(2);
  });

  it("shows the reviewer and comment for past decisions", async () => {
    const base = reviewDetail();
    await open(reviewDetail({
      status: "returnedForCompletion",
      availableDecisions: [],
      history: [...base.history, {
        fromStatus: "inProgress", toStatus: "returnedForCompletion", action: "returnForCompletion",
        occurredAt: "2026-10-16T08:00:00Z", comment: "أرفق خطاب الجهة المالكة", actorUserId: "r1",
        actorEmail: "reviewer@modon.local", actorIsApplicant: false,
      }],
    }));

    expect(within(historyBox()).getByText("reviewer@modon.local")).toBeInTheDocument();
    expect(within(historyBox()).getByText("أرفق خطاب الجهة المالكة")).toBeInTheDocument();
    expect(screen.getByText("لا توجد قرارات متاحة لهذه الحالة.")).toBeInTheDocument();
  });

  it("shows not-found for drafts and unknown requests", async () => {
    vi.mocked(api.getRequest).mockRejectedValue(new ApiError(404, "Not Found"));

    renderApp("/requests/999");

    expect(await screen.findByText("الطلب غير موجود أو لم يتم تقديمه بعد.")).toBeInTheDocument();
  });
});

describe("Review decisions", () => {
  beforeEach(() => useReviewerBackend());

  it("requires a reason before rejecting", async () => {
    await open();

    await userEvent.click(screen.getByRole("button", { name: "رفض الطلب" }));
    await userEvent.type(screen.getByLabelText(/سبب الرفض/), "   ");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(screen.getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    expect(screen.getByLabelText(/سبب الرفض/)).toHaveAttribute("aria-invalid", "true");
    expect(api.reject).not.toHaveBeenCalled();
  });

  it("requires a comment before returning for completion", async () => {
    await open();

    await userEvent.click(screen.getByRole("button", { name: "إعادة لإكمال النواقص" }));
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(screen.getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    expect(screen.getByText(/إمكانية التعديل وإعادة التقديم ستتوفر في المرحلة القادمة/)).toBeInTheDocument();
    expect(api.returnForCompletion).not.toHaveBeenCalled();
  });

  it("rejects with a trimmed reason and shows the new status", async () => {
    await open();
    vi.mocked(api.reject).mockResolvedValue(reviewDetail({ status: "rejected", availableDecisions: [] }));

    await userEvent.click(screen.getByRole("button", { name: "رفض الطلب" }));
    await userEvent.type(screen.getByLabelText(/سبب الرفض/), "  المخطط غير واضح  ");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(api.reject).toHaveBeenCalledWith(11, "المخطط غير واضح");
    expect(await screen.findByRole("status")).toHaveTextContent("تم رفض الطلب.");
    expect(screen.getAllByText("مرفوض").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  });

  it("approves without a note and returns with a comment", async () => {
    await open();
    vi.mocked(api.approve).mockResolvedValue(reviewDetail({ status: "approved", availableDecisions: [] }));

    await userEvent.click(screen.getByRole("button", { name: "اعتماد الطلب" }));
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(api.approve).toHaveBeenCalledWith(11, undefined);
    expect(await screen.findByRole("status")).toHaveTextContent("تم اعتماد الطلب.");
  });

  it("sends the return comment", async () => {
    await open();
    vi.mocked(api.returnForCompletion).mockResolvedValue(reviewDetail({ status: "returnedForCompletion", availableDecisions: [] }));

    await userEvent.click(screen.getByRole("button", { name: "إعادة لإكمال النواقص" }));
    await userEvent.type(screen.getByLabelText(/النواقص المطلوب إكمالها/), "أرفق خطاب الجهة المالكة");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(api.returnForCompletion).toHaveBeenCalledWith(11, "أرفق خطاب الجهة المالكة");
    expect(await screen.findByRole("status")).toHaveTextContent("تمت إعادة الطلب لمقدمه لإكمال النواقص.");
  });

  it("reloads and explains when another reviewer decided first", async () => {
    await open();
    vi.mocked(api.approve).mockRejectedValue(new ApiError(409, "requestChanged"));
    vi.mocked(api.getRequest).mockResolvedValue(reviewDetail({ status: "rejected", availableDecisions: [] }));

    await userEvent.click(screen.getByRole("button", { name: "اعتماد الطلب" }));
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تغيرت حالة الطلب قبل حفظ قرارك");
    expect(await screen.findByText("لا توجد قرارات متاحة لهذه الحالة.")).toBeInTheDocument();
    expect(api.getRequest).toHaveBeenCalledTimes(2);
  });

  it("shows the server's validation error", async () => {
    await open();
    vi.mocked(api.reject).mockRejectedValue(new ApiError(400, "invalid", { reason: ["tooLong"] }));

    await userEvent.click(screen.getByRole("button", { name: "رفض الطلب" }));
    await userEvent.type(screen.getByLabelText(/سبب الرفض/), "x");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(await screen.findByText("النص أطول من المسموح (2000 حرف)")).toBeInTheDocument();
  });
});
