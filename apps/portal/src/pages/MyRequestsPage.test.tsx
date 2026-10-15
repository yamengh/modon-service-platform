import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import type { RequestSummary } from "../api/types";
import { allAttachments, buildingLicence, completeExcavation, excavationPermit, excavationRequest, investor, renderApp } from "../test/render";
import { useExcavationBackend } from "../test/excavationMocks";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const summaries: RequestSummary[] = [
  {
    id: 8, requestNumber: "EXC-000008-15-10-26", serviceCode: "excavation-permit",
    serviceNameAr: "تصريح أعمال الحفر", serviceNameEn: "Excavation Works Permit", status: "draft",
    createdAt: "2026-10-15T12:00:00Z", updatedAt: "2026-10-15T12:00:00Z", submittedAt: null,
  },
  {
    id: 7, requestNumber: "EXC-000007-14-10-26", serviceCode: "excavation-permit",
    serviceNameAr: "تصريح أعمال الحفر", serviceNameEn: "Excavation Works Permit", status: "inProgress",
    createdAt: "2026-10-14T12:00:00Z", updatedAt: "2026-10-15T09:00:00Z", submittedAt: "2026-10-15T09:00:00Z",
  },
];

describe("Request tracking", () => {
  beforeEach(() => {
    useExcavationBackend();
  });

  it("lists my requests with number, service, status and dates", async () => {
    vi.mocked(api.listMyRequests).mockResolvedValue(summaries);

    renderApp("/requests");

    const rows = (await screen.findAllByRole("row")).slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText("EXC-000008-15-10-26")).toBeInTheDocument();
    expect(within(rows[0]).getByText("مسودة")).toBeInTheDocument();
    expect(within(rows[0]).getByRole("link", { name: "متابعة التعديل" })).toHaveAttribute("href", "/requests/8/edit");
    expect(within(rows[1]).getByText("قيد التنفيذ")).toBeInTheDocument();
    expect(within(rows[1]).getByText("تصريح أعمال الحفر")).toBeInTheDocument();
    expect(within(rows[1]).getByText("14/10/2026")).toBeInTheDocument();
    expect(within(rows[1]).getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/requests/7");
  });

  it("shows an empty state", async () => {
    vi.mocked(api.listMyRequests).mockResolvedValue([]);

    renderApp("/requests");

    expect(await screen.findByText(/لا توجد طلبات بعد/)).toBeInTheDocument();
  });

  it("shows an error when the list cannot load", async () => {
    vi.mocked(api.listMyRequests).mockRejectedValue(new Error("network"));

    renderApp("/requests");

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل البيانات");
  });

  it("shows a submitted request's details, attachments and status history", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(
      excavationRequest({
        status: "inProgress", canEdit: false, excavation: completeExcavation, attachments: allAttachments,
        submittedAt: "2026-10-15T11:00:00Z",
        history: [
          { fromStatus: null, toStatus: "draft", action: "create", occurredAt: "2026-10-15T10:00:00Z", comment: null },
          { fromStatus: "draft", toStatus: "inProgress", action: "submit", occurredAt: "2026-10-15T11:00:00Z", comment: null },
        ],
      }),
    );

    renderApp("/requests/7");

    expect(await screen.findByText("المدينة الصناعية الثانية بالرياض")).toBeInTheDocument();
    expect(screen.getByText(/مؤسسة البناء المتقدم للمقاولات – 7012345678/)).toBeInTheDocument();
    expect(screen.getByText("01/11/2026")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "workRoutePlan.pdf" }))
      .toHaveAttribute("href", "/api/modon/requests/7/attachments/workRoutePlan");
    const history = screen.getByRole("heading", { name: "سجل حالة الطلب" }).parentElement!;
    expect(within(history).getByText("إنشاء الطلب")).toBeInTheDocument();
    expect(within(history).getByText("تقديم الطلب")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "متابعة التعديل" })).not.toBeInTheDocument();
  });
});

describe("Start service", () => {
  beforeEach(() => {
    useExcavationBackend();
  });

  it("lets a contractor start an excavation permit and opens the form", async () => {
    vi.mocked(api.getService).mockResolvedValue(excavationPermit);
    vi.mocked(api.createRequest).mockResolvedValue(excavationRequest());
    vi.mocked(api.getRequest).mockResolvedValue(excavationRequest());
    renderApp("/services/excavation-permit");

    await userEvent.click(await screen.findByRole("button", { name: "بدء الخدمة" }));

    expect(api.createRequest).toHaveBeenCalledWith("excavation-permit");
    expect(await screen.findByRole("button", { name: "حفظ كمسودة" })).toBeInTheDocument();
  });

  it("keeps the building licence closed and says it comes later", async () => {
    vi.mocked(api.getPersonas).mockResolvedValue(investor);
    vi.mocked(api.getService).mockResolvedValue(buildingLicence);
    renderApp("/services/building-licence");

    expect(await screen.findByRole("button", { name: "بدء الخدمة" })).toBeDisabled();
    expect(screen.getByText(/وسيتوفر في مرحلة لاحقة من المشروع/)).toBeInTheDocument();
    expect(api.createRequest).not.toHaveBeenCalled();
  });

  it("shows an error when the draft cannot be created", async () => {
    vi.mocked(api.getService).mockResolvedValue(excavationPermit);
    vi.mocked(api.createRequest).mockRejectedValue(new ApiError(403, "roleNotAllowed"));
    renderApp("/services/excavation-permit");

    await userEvent.click(await screen.findByRole("button", { name: "بدء الخدمة" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر بدء الخدمة");
  });
});
