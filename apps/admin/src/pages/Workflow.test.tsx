import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import { renderApp, reviewDetail, summary, useReviewerBackend, workflowReady } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

describe("Workflow engine status on the dashboard", () => {
  beforeEach(() => {
    useReviewerBackend();
    vi.mocked(api.getSummary).mockResolvedValue(summary);
  });

  it("shows Camunda as ready with its version and the deployed process", async () => {
    renderApp("/");

    const card = await screen.findByRole("region", { name: "محرك سير العمل" });
    expect(within(card).getByText("جاهز")).toBeInTheDocument();
    expect(within(card).getByText("8.9.21")).toBeInTheDocument();
    expect(within(card).getByText("modon-excavation-permit v2")).toBeInTheDocument();
    expect(within(card).getByText("http://127.0.0.1:8080")).toBeInTheDocument();
  });

  it("says plainly when Camunda is not reachable, and still shows the counts", async () => {
    vi.mocked(api.getWorkflowStatus).mockResolvedValue({
      ...workflowReady,
      ready: false,
      summary: "Camunda is NOT reachable: Cannot reach Camunda at http://127.0.0.1:8080 (read cluster topology): connection refused",
      camunda: { ...workflowReady.camunda!, reachable: false, gatewayVersion: null, processDeployed: false, deployedVersion: null },
    });

    renderApp("/");

    const card = await screen.findByRole("region", { name: "محرك سير العمل" });
    expect(within(card).getByText("غير جاهز")).toBeInTheDocument();
    expect(within(card).getByText(/Camunda is NOT reachable/)).toBeInTheDocument();
    expect(within(card).getByText("غير منشورة")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "قيد التنفيذ: 4" })).toBeInTheDocument();
  });

  it("shows the in-process engine when Camunda is disabled", async () => {
    vi.mocked(api.getWorkflowStatus).mockResolvedValue({
      engine: "inProcess", ready: true, summary: "In-process workflow engine: Camunda is disabled.", camunda: null,
    });

    renderApp("/", "en");

    const card = await screen.findByRole("region", { name: "Workflow engine" });
    expect(within(card).getByText(/In-process \(Camunda disabled\)/)).toBeInTheDocument();
    expect(within(card).getByText("Ready")).toBeInTheDocument();
  });

  it("reports when the status cannot be read", async () => {
    vi.mocked(api.getWorkflowStatus).mockRejectedValue(new Error("network"));

    renderApp("/");

    expect(await screen.findByText("تعذر قراءة حالة محرك سير العمل.")).toBeInTheDocument();
  });
});

describe("Workflow on the request page", () => {
  beforeEach(() => useReviewerBackend());

  async function open(detail = reviewDetail()) {
    vi.mocked(api.getRequest).mockResolvedValue(detail);
    renderApp(`/requests/${detail.id}`);
    await screen.findByRole("heading", { name: "تصريح أعمال الحفر" });
  }

  it("shows the Camunda process instance and compares it with the database", async () => {
    await open();
    vi.mocked(api.getRequestWorkflow)
      .mockResolvedValueOnce({
        engine: "camunda", requestNumber: "EXC-000011-15-10-26", databaseStatus: "inProgress", processInstanceKey: "2251799813721500",
        processDefinitionVersion: 2, engineState: "ACTIVE", engineElementId: "Gateway_ModonReview", engineStatus: "inProgress", inSync: true, note: null,
      })
      .mockResolvedValueOnce({
        engine: "camunda", requestNumber: "EXC-000011-15-10-26", databaseStatus: "inProgress", processInstanceKey: "2251799813721500",
        processDefinitionVersion: 2, engineState: "ACTIVE", engineElementId: "Event_AwaitCoordinationLetters",
        engineStatus: "awaitingCoordinationLetters", inSync: false, note: null,
      });

    expect(screen.getByText("2251799813721500")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "مقارنة الحالة مع Camunda" }));
    expect(await screen.findByText(/متطابق: حالة الطلب في Camunda هي نفسها/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "مقارنة الحالة مع Camunda" }));
    expect(await screen.findByText(/غير متطابق: حالة الطلب في Camunda هي بانتظار خطابات التنسيق/)).toBeInTheDocument();
    expect(api.getRequestWorkflow).toHaveBeenCalledWith(11);
  });

  it("explains a Camunda outage instead of a generic error, and keeps the request as it was", async () => {
    await open();
    vi.mocked(api.approve).mockRejectedValue(new ApiError(503, "workflowUnavailable"));

    await userEvent.click(screen.getByRole("button", { name: "اعتماد الطلب" }));
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر الوصول إلى محرك سير العمل (Camunda)، ولم يُحفظ القرار");
    expect(screen.getAllByText("قيد التنفيذ").length).toBeGreaterThan(0);
    expect(api.getRequest).toHaveBeenCalledTimes(1); // not treated as "someone else decided"
  });

  it("explains a request that is not linked to a Camunda process", async () => {
    await open(reviewDetail({ workflow: { engine: "inProcess", processInstanceKey: null, processDefinitionVersion: null } }));
    vi.mocked(api.reject).mockRejectedValue(new ApiError(409, "workflowNotStarted"));

    expect(screen.queryByRole("button", { name: "مقارنة الحالة مع Camunda" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "رفض الطلب" }));
    await userEvent.type(screen.getByLabelText(/سبب الرفض/), "سبب");
    await userEvent.click(screen.getByRole("button", { name: "تأكيد القرار" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("هذا الطلب غير مرتبط بعملية في Camunda");
  });
});
