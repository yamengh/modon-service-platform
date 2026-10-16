import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api } from "../api/client";
import { queueItem, renderApp, summary, useReviewerBackend } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const page = (items = [queueItem()], total = items.length) => ({ items, total, page: 1, pageSize: 20 });

describe("Dashboard", () => {
  beforeEach(() => useReviewerBackend());

  it("shows request counts by status with links to the filtered queue", async () => {
    vi.mocked(api.getSummary).mockResolvedValue(summary);

    renderApp("/");

    expect(await screen.findByRole("link", { name: "إجمالي الطلبات المقدمة: 9" })).toHaveAttribute("href", "/requests");
    expect(screen.getByRole("link", { name: "قيد التنفيذ: 4" })).toHaveAttribute("href", "/requests?status=inProgress");
    expect(screen.getByRole("link", { name: "إكمال النواقص: 2" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "مرفوض: 1" })).toBeInTheDocument();
  });

  it("shows an error with a retry button", async () => {
    vi.mocked(api.getSummary).mockRejectedValueOnce(new Error("network")).mockResolvedValue(summary);
    renderApp("/");

    await userEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));

    expect(await screen.findByRole("link", { name: "قيد التنفيذ: 4" })).toBeInTheDocument();
  });
});

describe("Request queue", () => {
  beforeEach(() => useReviewerBackend());

  it("lists number, service, applicant, submission date, status and industrial city", async () => {
    vi.mocked(api.listRequests).mockResolvedValue(page([
      queueItem(),
      queueItem({ id: 12, requestNumber: "EXC-000012-14-10-26", status: "approved", submittedAt: "2026-10-14T10:00:00Z", industrialCityCode: "unknown-city", establishment: null }),
    ]));

    renderApp("/requests");

    const rows = (await screen.findAllByRole("row")).slice(1);
    expect(rows).toHaveLength(2);
    const first = within(rows[0]);
    expect(first.getByRole("link", { name: "EXC-000011-15-10-26" })).toHaveAttribute("href", "/requests/11");
    expect(first.getByText("تصريح أعمال الحفر")).toBeInTheDocument();
    expect(first.getByText("مؤسسة البناء المتقدم للمقاولات")).toBeInTheDocument();
    expect(first.getByText("contractor@modon.local")).toBeInTheDocument();
    expect(first.getByText("15/10/2026")).toBeInTheDocument();
    expect(first.getByText("قيد التنفيذ")).toBeInTheDocument();
    expect(await first.findByText("المدينة الصناعية الثانية بالرياض")).toBeInTheDocument();
    expect(within(rows[1]).getByText("معتمد")).toBeInTheDocument();
    expect(within(rows[1]).getByText("unknown-city")).toBeInTheDocument(); // unknown codes stay visible
    expect(screen.getByText("صفحة 1 من 1 · 2 طلب")).toBeInTheDocument();
  });

  it("filters by status and service, searches by number, and clears filters", async () => {
    vi.mocked(api.listRequests).mockResolvedValue(page());
    renderApp("/requests");
    await screen.findAllByRole("row");
    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "", service: "", search: "", page: 1 });

    await userEvent.selectOptions(screen.getByLabelText("الحالة"), "returnedForCompletion");
    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "returnedForCompletion", service: "", search: "", page: 1 });

    await screen.findByRole("option", { name: "تصريح أعمال الحفر" });
    await userEvent.selectOptions(screen.getByLabelText("الخدمة"), "excavation-permit");
    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "returnedForCompletion", service: "excavation-permit", search: "", page: 1 });

    await userEvent.type(screen.getByLabelText("بحث برقم الطلب"), " exc-000011 ");
    await userEvent.click(screen.getByRole("button", { name: "بحث" }));
    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "returnedForCompletion", service: "excavation-permit", search: "exc-000011", page: 1 });

    await userEvent.click(screen.getByRole("button", { name: "مسح التصفية" }));
    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "", service: "", search: "", page: 1 });
  });

  it("reads filters from the URL and pages through results", async () => {
    vi.mocked(api.listRequests).mockResolvedValue({ items: [queueItem()], total: 45, page: 1, pageSize: 20 });
    renderApp("/requests?status=inProgress");

    expect(await screen.findByText("صفحة 1 من 3 · 45 طلب")).toBeInTheDocument();
    expect(screen.getByLabelText("الحالة")).toHaveValue("inProgress");
    expect(screen.getByRole("button", { name: "السابق" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "التالي" }));

    expect(api.listRequests).toHaveBeenLastCalledWith({ status: "inProgress", service: "", search: "", page: 2 });
  });

  it("shows empty and error states", async () => {
    vi.mocked(api.listRequests).mockResolvedValueOnce(page([])).mockRejectedValueOnce(new Error("network"));
    renderApp("/requests");

    expect(await screen.findByText("لا توجد طلبات مطابقة.")).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("الحالة"), "approved");
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل البيانات");
  });
});

describe("Dashboard counts come from the server", () => {
  beforeEach(() => useReviewerBackend());

  it("does not compute anything client-side", async () => {
    vi.mocked(api.getSummary).mockResolvedValue({ ...summary, total: 0, byStatus: { ...summary.byStatus, inProgress: 0 } });

    renderApp("/");

    expect(await screen.findByRole("link", { name: "إجمالي الطلبات المقدمة: 0" })).toBeInTheDocument();
    expect(api.listRequests).not.toHaveBeenCalled();
  });
});
