import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import { contractor, renderApp, reviewer, summary, useReviewerBackend } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

describe("Staff authentication and authorization", () => {
  beforeEach(() => {
    useReviewerBackend();
    vi.mocked(api.getSummary).mockResolvedValue(summary);
  });

  it("sends anonymous users to the staff sign-in page", async () => {
    vi.mocked(api.getMe).mockResolvedValue(null);

    renderApp("/requests");

    expect(await screen.findByRole("heading", { name: "دخول الموظفين" })).toBeInTheDocument();
    expect(api.listRequests).not.toHaveBeenCalled();
  });

  it("blocks investors and contractors even after they sign in", async () => {
    vi.mocked(api.getMe).mockResolvedValue(contractor);

    renderApp("/");

    expect(await screen.findByRole("alert")).toHaveTextContent("لا تملك صلاحية المراجعة");
    expect(api.getSummary).not.toHaveBeenCalled();
    expect(api.listRequests).not.toHaveBeenCalled();
  });

  it("lets a reviewer sign in and opens the dashboard", async () => {
    vi.mocked(api.getMe).mockResolvedValueOnce(null).mockResolvedValue(reviewer);
    vi.mocked(api.login).mockResolvedValue();
    renderApp("/login");
    await screen.findByRole("heading", { name: "دخول الموظفين" });

    await userEvent.type(screen.getByLabelText("البريد الإلكتروني"), "reviewer@modon.local");
    await userEvent.type(screen.getByLabelText("كلمة المرور"), "Modon@Dev1");
    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));

    expect(api.login).toHaveBeenCalledWith("reviewer@modon.local", "Modon@Dev1");
    expect(await screen.findByRole("heading", { name: "لوحة المتابعة" })).toBeInTheDocument();
    expect(screen.getByText("مراجع")).toBeInTheDocument();
  });

  it("validates the sign-in form and reports wrong credentials", async () => {
    vi.mocked(api.getMe).mockResolvedValue(null);
    vi.mocked(api.login).mockRejectedValue(new ApiError(400, "invalid"));
    renderApp("/login");
    await screen.findByRole("heading", { name: "دخول الموظفين" });

    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
    expect(screen.getByText("البريد الإلكتروني مطلوب")).toBeInTheDocument();
    expect(api.login).not.toHaveBeenCalled();

    // the field errors are inside the labels now, so match the label's start
    await userEvent.type(screen.getByLabelText(/^البريد الإلكتروني/), "reviewer@modon.local");
    await userEvent.type(screen.getByLabelText(/^كلمة المرور/), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تسجيل الدخول");
  });

  it("says the server is unreachable instead of blaming the password when the backend is down", async () => {
    vi.mocked(api.getMe).mockResolvedValue(null);
    // The Vite proxy answers 500 when nothing listens on the API port; a dropped connection rejects fetch.
    vi.mocked(api.login).mockRejectedValueOnce(new ApiError(500, "Internal Server Error"))
      .mockRejectedValueOnce(new TypeError("Failed to fetch"));
    renderApp("/login");
    await screen.findByRole("heading", { name: "دخول الموظفين" });
    await userEvent.type(screen.getByLabelText("البريد الإلكتروني"), "reviewer@modon.local");
    await userEvent.type(screen.getByLabelText("كلمة المرور"), "Modon@Dev1");

    for (let attempt = 0; attempt < 2; attempt++) {
      await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
      const alert = await screen.findByRole("alert");
      expect(alert).toHaveTextContent("تعذر الاتصال بالخادم");
      expect(alert).not.toHaveTextContent("تحقق من البريد الإلكتروني وكلمة المرور");
    }
    expect(api.login).toHaveBeenCalledTimes(2);
  });

  it("switches to English and left-to-right layout", async () => {
    renderApp("/");
    await screen.findByRole("heading", { name: "لوحة المتابعة" });

    await userEvent.click(screen.getByRole("button", { name: "English" }));

    expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("ltr");
  });
});
