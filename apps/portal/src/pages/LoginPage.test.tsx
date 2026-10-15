import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import { investor, renderApp } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    api: { login: vi.fn(), logout: vi.fn(), getPersonas: vi.fn(), listServices: vi.fn(), getService: vi.fn() },
  };
});

async function openLogin() {
  renderApp("/login");
  await screen.findByRole("heading", { name: "تسجيل الدخول" });
}

describe("LoginPage", () => {
  beforeEach(() => {
    vi.mocked(api.getPersonas).mockResolvedValue(null);
    vi.mocked(api.listServices).mockResolvedValue([]);
  });

  it("shows required-field errors and does not call the API", async () => {
    await openLogin();

    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));

    expect(screen.getByText("البريد الإلكتروني مطلوب")).toBeInTheDocument();
    expect(screen.getByText("كلمة المرور مطلوبة")).toBeInTheDocument();
    expect(api.login).not.toHaveBeenCalled();
  });

  it("rejects a malformed email", async () => {
    await openLogin();

    await userEvent.type(screen.getByLabelText("البريد الإلكتروني"), "not-an-email");
    await userEvent.type(screen.getByLabelText("كلمة المرور"), "secret");
    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));

    expect(screen.getByText("صيغة البريد الإلكتروني غير صحيحة")).toBeInTheDocument();
    expect(api.login).not.toHaveBeenCalled();
  });

  it("signs in and opens the services page", async () => {
    await openLogin();
    vi.mocked(api.login).mockResolvedValue();
    vi.mocked(api.getPersonas).mockResolvedValue(investor);

    await userEvent.type(screen.getByLabelText("البريد الإلكتروني"), "  investor@modon.local ");
    await userEvent.type(screen.getByLabelText("كلمة المرور"), "Modon@Dev1");
    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));

    expect(api.login).toHaveBeenCalledWith("investor@modon.local", "Modon@Dev1");
    expect(await screen.findByRole("heading", { name: "جميع الخدمات" })).toBeInTheDocument();
  });

  it("shows an error when the backend rejects the credentials", async () => {
    await openLogin();
    vi.mocked(api.login).mockRejectedValue(new ApiError(400, "Invalid credentials"));

    await userEvent.type(screen.getByLabelText("البريد الإلكتروني"), "investor@modon.local");
    await userEvent.type(screen.getByLabelText("كلمة المرور"), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تسجيل الدخول");
  });
});
