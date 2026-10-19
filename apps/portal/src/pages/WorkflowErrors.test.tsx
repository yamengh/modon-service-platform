import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import type { RequestDetail } from "../api/types";
import { allAttachments, completeExcavation, coordinationLetterSlots, excavationRequest, renderApp } from "../test/render";
import { useExcavationBackend } from "../test/excavationMocks";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const user = () => userEvent.setup({ applyAccept: false });

const draft = (overrides: Partial<RequestDetail> = {}) =>
  excavationRequest({ excavation: completeExcavation, attachments: allAttachments, ...overrides });

const returned = () =>
  draft({
    status: "returnedForCompletion", submittedAt: "2026-10-15T11:00:00Z",
    currentTask: { kind: "completeReturnedRequest", instructions: "صحح الموقع", assignedAt: "2026-10-16T09:00:00Z" },
  });

describe("When Modon's workflow engine (Camunda) is not available", () => {
  beforeEach(() => useExcavationBackend());

  it("keeps a draft on the form and explains that nothing was submitted", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(draft());
    vi.mocked(api.saveExcavation).mockResolvedValue(draft());
    vi.mocked(api.submitRequest).mockRejectedValue(new ApiError(503, "workflowUnavailable"));
    renderApp("/requests/7/edit");

    await user().click(await screen.findByRole("button", { name: "تقديم الطلب" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("خدمة سير العمل في مدن غير متاحة حالياً، ولم يُرسَل شيء");
    expect(api.saveExcavation).toHaveBeenCalled(); // the edits were saved first
    expect(screen.getByRole("button", { name: "تقديم الطلب" })).toBeEnabled(); // still on the form, can retry
  });

  it("explains the same on resubmission of a returned request", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(returned());
    vi.mocked(api.saveExcavation).mockResolvedValue(returned());
    vi.mocked(api.submitRequest).mockRejectedValue(new ApiError(503, "workflowUnavailable"));
    renderApp("/requests/7/edit");

    await user().click(await screen.findByRole("button", { name: "إعادة تقديم الطلب" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("ولم يُرسَل شيء");
    expect(screen.getByRole("button", { name: "إعادة تقديم الطلب" })).toBeInTheDocument();
  });

  it("explains it when sending coordination letters", async () => {
    const letters = coordinationLetterSlots.map((s) => ({
      slotCode: s.code, fileName: `${s.code}.pdf`, contentType: "application/pdf", sizeBytes: 2048, uploadedAt: "2026-10-16T10:00:00Z",
    }));
    vi.mocked(api.getRequest).mockResolvedValue(draft({
      status: "awaitingCoordinationLetters", canEdit: false, canUploadCoordinationLetters: true, submittedAt: "2026-10-15T11:00:00Z",
      attachments: [...allAttachments, ...letters],
      currentTask: { kind: "uploadCoordinationLetters", instructions: null, assignedAt: "2026-10-16T09:00:00Z" },
    }));
    vi.mocked(api.submitCoordinationLetters).mockRejectedValue(new ApiError(503, "workflowUnavailable"));
    renderApp("/requests/7/coordination-letters");

    await user().click(await screen.findByRole("button", { name: "إرسال خطابات التنسيق" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("ولم يُرسَل شيء");
    // The uploaded letters are still listed, ready to send again.
    expect(within(document.querySelector('[data-slot="secCoordinationLetter"]') as HTMLElement)
      .getByRole("link", { name: "secCoordinationLetter.pdf" })).toBeInTheDocument();
  });

  it("explains a request that cannot continue in the workflow", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(returned());
    vi.mocked(api.saveExcavation).mockResolvedValue(returned());
    vi.mocked(api.submitRequest).mockRejectedValue(new ApiError(409, "workflowNotStarted"));
    renderApp("/requests/7/edit");

    await user().click(await screen.findByRole("button", { name: "إعادة تقديم الطلب" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("لا يمكن متابعة هذا الطلب إلكترونياً حالياً");
  });
});
