import { screen } from "@testing-library/react";
import { api } from "../api/client";
import { investor, renderApp, reviewer } from "../test/render";

vi.mock("../api/client", () => ({
  api: { login: vi.fn(), logout: vi.fn(), getPersonas: vi.fn(), listServices: vi.fn(), getService: vi.fn() },
}));

describe("RequireAuth", () => {
  beforeEach(() => {
    vi.mocked(api.listServices).mockResolvedValue([]);
  });

  it("redirects anonymous users to the login page", async () => {
    vi.mocked(api.getPersonas).mockResolvedValue(null);

    renderApp("/services");

    expect(await screen.findByRole("heading", { name: "تسجيل الدخول" })).toBeInTheDocument();
    expect(api.listServices).not.toHaveBeenCalled();
  });

  it("blocks signed-in users that have no portal persona", async () => {
    vi.mocked(api.getPersonas).mockResolvedValue(reviewer);

    renderApp("/services");

    expect(await screen.findByRole("alert")).toHaveTextContent("لا يمكنك استخدام بوابة الشركاء");
    expect(api.listServices).not.toHaveBeenCalled();
  });

  it("lets an investor reach the services page", async () => {
    vi.mocked(api.getPersonas).mockResolvedValue(investor);

    renderApp("/services");

    expect(await screen.findByRole("heading", { name: "جميع الخدمات" })).toBeInTheDocument();
    expect(screen.getByText("مستثمر")).toBeInTheDocument();
  });
});
