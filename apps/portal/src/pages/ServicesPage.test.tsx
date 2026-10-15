import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api } from "../api/client";
import { buildingLicence, excavationPermit, investor, renderApp } from "../test/render";

vi.mock("../api/client", () => ({
  api: { login: vi.fn(), logout: vi.fn(), getPersonas: vi.fn(), listServices: vi.fn(), getService: vi.fn() },
}));

describe("Service catalogue", () => {
  beforeEach(() => {
    vi.mocked(api.getPersonas).mockResolvedValue(investor);
    vi.mocked(api.listServices).mockResolvedValue([buildingLicence, excavationPermit]);
  });

  it("lists the services in Arabic with their beneficiary", async () => {
    renderApp("/services");

    const cards = await screen.findAllByRole("listitem");
    expect(cards).toHaveLength(2);
    expect(within(cards[0]).getByRole("heading")).toHaveTextContent("رخصة البناء وتقارير البناء");
    expect(within(cards[1]).getByText("المقاول")).toBeInTheDocument();
    expect(within(cards[1]).getByText(/3 أيام عمل/)).toBeInTheDocument();
  });

  it("switches the catalogue to English", async () => {
    renderApp("/services");
    await screen.findAllByRole("listitem");

    await userEvent.click(screen.getByRole("button", { name: "English" }));

    expect(screen.getByRole("heading", { name: "Excavation Works Permit" })).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("ltr");
  });

  it("shows an error message when the catalogue cannot load", async () => {
    vi.mocked(api.listServices).mockRejectedValue(new Error("network"));

    renderApp("/services");

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل البيانات");
  });

  it("shows service details with steps, documents and a disabled start button", async () => {
    vi.mocked(api.getService).mockResolvedValue(excavationPermit);

    renderApp("/services/excavation-permit");

    expect(await screen.findByRole("heading", { name: "تصريح أعمال الحفر" })).toBeInTheDocument();
    expect(api.getService).toHaveBeenCalledWith("excavation-permit");
    expect(screen.getAllByRole("listitem").map((li) => li.textContent)).toEqual([
      "الدخول على بوابة مدن",
      "اختيار خدمات مدن",
      "اختيار تصريح أعمال الحفر",
      "مخطط مسار العمل",
      "خطة سلامة السير",
    ]);
    expect(screen.getByRole("button", { name: "بدء الخدمة" })).toBeDisabled();
    // the investor is viewing a contractor service
    expect(screen.getByText("هذه الخدمة مخصصة لفئة أخرى من المستخدمين.")).toBeInTheDocument();
  });

  it("shows not-found for an unknown service code", async () => {
    vi.mocked(api.getService).mockResolvedValue(null);

    renderApp("/services/unknown");

    expect(await screen.findByText("الخدمة غير موجودة.")).toBeInTheDocument();
  });
});
