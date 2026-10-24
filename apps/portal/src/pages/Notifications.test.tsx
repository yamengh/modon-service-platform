import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, notificationsApi } from "../api/client";
import type { NotificationPage, NotificationView } from "../api/types";
import { contractor, renderApp } from "../test/render";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])),
    notificationsApi: Object.fromEntries(Object.keys(actual.notificationsApi).map((k) => [k, vi.fn()])),
  };
});

const returned: NotificationView = {
  id: 41, kind: "requestUpdate", requestId: 7, requestNumber: "EXC-000007-15-10-26", serviceCode: "excavation-permit",
  action: "returnForCompletion", status: "returnedForCompletion",
  titleAr: "أعادت مدن الطلب لإكمال النواقص — EXC-000007-15-10-26", titleEn: "Modon returned the request for completion — EXC-000007-15-10-26",
  comment: "صحح موقع العمل", link: "/requests/7", createdAt: "2026-10-16T09:00:00Z", read: false,
};
const invited: NotificationView = {
  ...returned, id: 40, kind: "officeInvited", requestId: 12, requestNumber: "BLR-000012-18-10-26", serviceCode: "building-licence",
  action: "selectContractor", status: "awaitingContractorDecision",
  titleAr: "اختارك مستثمر لطلب رخصة بناء — بانتظار ردك — BLR-000012-18-10-26",
  titleEn: "An investor chose your office for a building licence — please answer — BLR-000012-18-10-26",
  comment: null, link: "/projects/12", read: true,
};
const page = (items: NotificationView[], unreadCount: number): NotificationPage => ({ items, total: items.length, unreadCount, page: 1, pageSize: 20 });

beforeEach(() => {
  vi.mocked(api.getPersonas).mockResolvedValue(contractor);
  vi.mocked(api.listServices).mockResolvedValue([]);
  vi.mocked(notificationsApi.unreadCount).mockResolvedValue(1);
  vi.mocked(notificationsApi.list).mockResolvedValue(page([returned, invited], 1));
  vi.mocked(notificationsApi.markRead).mockResolvedValue(undefined);
  vi.mocked(notificationsApi.markAllRead).mockResolvedValue(undefined);
});

describe("Notifications", () => {
  it("shows the unread count in the navigation", async () => {
    renderApp("/services");

    const link = await screen.findByRole("link", { name: "الإشعارات (1)" });
    expect(link).toHaveAttribute("href", "/notifications");
    expect(within(link).getByText("1")).toBeInTheDocument();
  });

  it("lists my notifications with the reviewer comment, newest first, and links to the request or project", async () => {
    renderApp("/notifications");

    const items = await screen.findAllByRole("listitem");
    expect(within(items[0]).getByText(/أعادت مدن الطلب لإكمال النواقص/)).toBeInTheDocument();
    expect(within(items[0]).getByText("صحح موقع العمل")).toBeInTheDocument();
    expect(within(items[0]).getByText("جديد")).toBeInTheDocument();
    expect(within(items[0]).getByRole("link", { name: "فتح" })).toHaveAttribute("href", "/requests/7");
    expect(within(items[1]).getByRole("link", { name: "فتح" })).toHaveAttribute("href", "/projects/12");
    expect(screen.getByText(/لا تُرسل رسائل بريد إلكتروني أو SMS فعلية/)).toBeInTheDocument();
  });

  it("marks one as read when opened, and all with one button", async () => {
    vi.mocked(api.getRequest).mockRejectedValue(new Error("not needed"));
    renderApp("/notifications");

    await userEvent.click(within((await screen.findAllByRole("listitem"))[0]).getByRole("link", { name: "فتح" }));
    expect(notificationsApi.markRead).toHaveBeenCalledWith(41);

    renderApp("/notifications");
    await userEvent.click(await screen.findByRole("button", { name: "تحديد الكل كمقروء" }));
    expect(notificationsApi.markAllRead).toHaveBeenCalled();
  });

  it("filters to unread ones, shows an empty state, and works in English", async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue(page([], 0));
    renderApp("/notifications", "en");

    expect(await screen.findByText("No notifications.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("checkbox", { name: "Unread only" }));
    expect(notificationsApi.list).toHaveBeenLastCalledWith(1, true);
  });

  it("shows an error when they cannot load", async () => {
    vi.mocked(notificationsApi.list).mockRejectedValue(new Error("network"));
    renderApp("/notifications");

    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل البيانات");
  });
});
