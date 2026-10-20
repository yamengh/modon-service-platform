import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { api, ApiError } from "../api/client";
import type { TaskItem } from "../api/types";
import { establishments, investor, renderApp } from "../test/render";
import {
  building,
  buildingRequest,
  completeBuilding,
  consultantOffice,
  consultantPersona,
  inConsultantSelection,
  otherOffice,
  project,
  quotation,
  tender,
} from "../test/buildingMocks";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, api: Object.fromEntries(Object.keys(actual.api).map((k) => [k, vi.fn()])) };
});

const user = () => userEvent.setup({ applyAccept: false });

beforeEach(() => {
  vi.mocked(api.getPersonas).mockResolvedValue(investor);
  vi.mocked(api.myEstablishments).mockResolvedValue([
    ...establishments,
    { id: 2, crNumber: "7098765432", nameAr: "مصنع الشرق للصناعات", nameEn: "Al-Sharq Industries Factory" },
  ]);
  vi.mocked(api.listLookups).mockResolvedValue([]);
  vi.mocked(api.attachmentUrl).mockImplementation((id, slot) => `/api/modon/requests/${id}/attachments/${slot}`);
  vi.mocked(api.buildingFileUrl).mockImplementation((id, kind, fileId) => `/api/modon/requests/${id}/building/${kind}/${fileId}/attachment`);
  vi.mocked(api.projectFileUrl).mockImplementation((id, kind, fileId) => `/api/modon/building/projects/${id}/${kind}/${fileId}/attachment`);
  vi.mocked(api.projectAttachmentUrl).mockImplementation((id, slot) => `/api/modon/building/projects/${id}/attachments/${slot}`);
});

describe("Building licence application form", () => {
  it("shows the manual's fields, checks required ones before submitting, and calls nothing", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(buildingRequest());
    renderApp("/requests/12/building");

    expect(await screen.findByRole("heading", { name: "طلب رخصة بناء" })).toBeInTheDocument();
    for (const label of ["السجل التجاري", "رقم العقد / رقم التسجيل", "نوع الرخصة", "ميزانية الإنشاء", "عدد الأدوار", "إطار العمل", "ملاحظات إضافية"])
      expect(screen.getByLabelText(new RegExp(label))).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "ترخيص بناء جديد" })).toBeInTheDocument();

    await user().click(screen.getByRole("button", { name: "تقديم الطلب" }));

    expect(screen.getByText(/تعذر تقديم الطلب/)).toBeInTheDocument();
    expect(screen.getByLabelText(/رقم العقد/)).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("checkbox", { name: /للتحقق من الأحكام والشروط/ })).toHaveAttribute("aria-invalid", "true");
    expect(api.saveBuilding).not.toHaveBeenCalled();
    expect(api.submitRequest).not.toHaveBeenCalled();
  });

  it("saves a draft with contacts, and submits (save, then submit) to consultant selection", async () => {
    vi.mocked(api.getRequest).mockResolvedValueOnce(
      buildingRequest({
        building: building({ form: { ...completeBuilding, contacts: [] } }),
        attachments: [
          { slotCode: "approvedGeneralPlans", fileName: "g.pdf", contentType: "application/pdf", sizeBytes: 10, uploadedAt: "2026-10-18T08:00:00Z" },
          { slotCode: "approvedSafetyPlans", fileName: "s.pdf", contentType: "application/pdf", sizeBytes: 10, uploadedAt: "2026-10-18T08:00:00Z" },
        ],
      }),
    );
    vi.mocked(api.saveBuilding).mockResolvedValue(buildingRequest());
    vi.mocked(api.submitRequest).mockResolvedValue(inConsultantSelection());
    vi.mocked(api.getRequest).mockResolvedValue(inConsultantSelection());
    renderApp("/requests/12/building");

    await user().click(await screen.findByRole("button", { name: "إضافة جهة اتصال" }));
    const row = within(screen.getByTestId("contact-row"));
    await user().type(row.getByLabelText("الإسم"), "Sameh");
    await user().type(row.getByLabelText("البريد الإلكتروني"), "sameh@example.com");
    await user().type(row.getByLabelText("رقم الجوال"), "01201001203");
    await user().click(screen.getByRole("button", { name: "حفظ كمسودة" }));

    expect(await screen.findByRole("status")).toHaveTextContent("تم حفظ المسودة");
    expect(api.saveBuilding).toHaveBeenLastCalledWith(12, {
      ...completeBuilding,
      contacts: [{ name: "Sameh", email: "sameh@example.com", mobile: "01201001203" }],
    });

    await user().click(screen.getByRole("button", { name: "تقديم الطلب" }));

    expect(api.submitRequest).toHaveBeenCalledWith(12);
    expect(await screen.findByText("تم تقديم معلومات الطلب الأساسية. اختر الآن المكتب الاستشاري.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "اختيار المكتب الإستشاري" })).toBeInTheDocument();
  });

  it("shows the backend's field errors, including a contact row's", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(buildingRequest({ building: building({ form: { ...completeBuilding, termsAccepted: false } }) }));
    vi.mocked(api.saveBuilding).mockRejectedValue(new ApiError(400, "validation", { "contacts[0].email": ["invalidEmail"] }));
    renderApp("/requests/12/building");

    await user().click(await screen.findByRole("button", { name: "حفظ كمسودة" }));

    expect(await screen.findByText("صيغة البريد الإلكتروني غير صحيحة")).toBeInTheDocument();
  });
});

describe("Investor: consultant and contractor stages", () => {
  it("chooses a specific consulting office from the registered offices", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(inConsultantSelection());
    vi.mocked(api.listOffices).mockResolvedValue([consultantOffice, otherOffice]);
    vi.mocked(api.chooseOffice).mockResolvedValue(
      inConsultantSelection({
        status: "awaitingConsultantDecision",
        building: building({ tenders: [tender({ mode: "direct", number: null, invitedOffice: consultantOffice })] }),
        currentTask: null,
      }),
    );
    renderApp("/requests/12");

    const panel = within((await screen.findByRole("heading", { name: "اختيار المكتب الإستشاري" })).closest("section")!);
    await user().click(panel.getByLabelText("اختيار مكتب إستشاري محدد"));
    await user().click(panel.getByRole("button", { name: "التقدم في الطلب" }));
    expect(panel.getByText("هذا الحقل مطلوب")).toBeInTheDocument(); // the office is required
    await user().selectOptions(panel.getByRole("combobox"), String(consultantOffice.id));
    await user().click(panel.getByRole("button", { name: "التقدم في الطلب" }));

    expect(api.listOffices).toHaveBeenCalledWith("consultant");
    expect(api.chooseOffice).toHaveBeenCalledWith(12, "consultant", "direct", consultantOffice.id);
    expect(await screen.findByText("بانتظار رد المكتب الهندسي الاستشاري.")).toBeInTheDocument();
  });

  it("opens the project to all offices (المشروع)", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(inConsultantSelection());
    vi.mocked(api.chooseOffice).mockResolvedValue(
      inConsultantSelection({ status: "consultantTender", building: building({ tenders: [tender()], investorActions: ["acceptConsultantQuotation", "rejectConsultantQuotations"] }) }),
    );
    renderApp("/requests/12");

    await user().click(await screen.findByRole("button", { name: "التقدم في الطلب" }));

    expect(api.chooseOffice).toHaveBeenCalledWith(12, "consultant", "openTender", undefined);
    const panel = within((await screen.findByRole("heading", { name: "عروض الأسعار" })).closest("section")!);
    expect(panel.getByText(/لم تصل عروض أسعار بعد/)).toBeInTheDocument();
    expect(panel.getByText("Bid-00400-18-10-26")).toBeInTheDocument();
  });

  it("lists quotations, chooses one, or rejects them all with a reason", async () => {
    const withQuotes = inConsultantSelection({
      status: "consultantTender",
      currentTask: { kind: "reviewConsultantQuotations", instructions: null, assignedAt: "2026-10-18T10:00:00Z" },
      building: building({
        tenders: [tender({ quotations: [quotation(), quotation({ id: 502, office: otherOffice, amountSar: 180_000, comment: null })] })],
        investorActions: ["acceptConsultantQuotation", "rejectConsultantQuotations"],
      }),
    });
    vi.mocked(api.getRequest).mockResolvedValue(withQuotes);
    vi.mocked(api.acceptQuotation).mockResolvedValue(inConsultantSelection({ status: "contractorSelection", building: building({ investorActions: ["openContractorTender", "selectContractor"] }) }));
    vi.mocked(api.rejectQuotations).mockResolvedValue(inConsultantSelection());
    renderApp("/requests/12");

    const panel = within((await screen.findByRole("heading", { name: "عروض الأسعار" })).closest("section")!);
    const table = within(panel.getByRole("table"));
    expect(table.getByText("200,000 ريال")).toBeInTheDocument();
    expect(table.getByText("180,000 ريال")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "إختيار عرض سعر" })).toBeDisabled();
    await user().click(table.getByRole("radio", { name: "مكتب التصميم" }));
    await user().click(screen.getByRole("button", { name: "إختيار عرض سعر" }));
    expect(api.acceptQuotation).toHaveBeenCalledWith(12, "consultant", 502);
    expect(await screen.findByRole("heading", { name: "اختيار المقاول" })).toBeInTheDocument();
  });

  it("rejects all quotations / cancels the project", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(
      inConsultantSelection({
        status: "consultantTender",
        building: building({ tenders: [tender({ quotations: [quotation()] })], investorActions: ["acceptConsultantQuotation", "rejectConsultantQuotations"] }),
      }),
    );
    vi.mocked(api.rejectQuotations).mockResolvedValue(inConsultantSelection());
    renderApp("/requests/12");

    await user().click(await screen.findByRole("button", { name: "رفض جميع العروض / إلغاء المشروع" }));
    await user().type(screen.getByLabelText("سبب الرفض (اختياري)"), "الأسعار مرتفعة");
    await user().click(screen.getByRole("button", { name: "تأكيد الرفض" }));

    expect(api.rejectQuotations).toHaveBeenCalledWith(12, "consultant", "الأسعار مرتفعة");
    expect(await screen.findByRole("heading", { name: "اختيار المكتب الإستشاري" })).toBeInTheDocument();
  });

  it("reloads and explains when the request changed (409)", async () => {
    vi.mocked(api.getRequest).mockResolvedValueOnce(inConsultantSelection());
    vi.mocked(api.getRequest).mockResolvedValue(inConsultantSelection({ status: "consultantTender", building: building({ tenders: [tender()] }) }));
    vi.mocked(api.chooseOffice).mockRejectedValue(new ApiError(409, "invalidTransition"));
    renderApp("/requests/12");

    await user().click(await screen.findByRole("button", { name: "التقدم في الطلب" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("تغيرت حالة الطلب");
    expect(api.getRequest).toHaveBeenCalledTimes(2);
  });

  it("shows the workflow-unavailable message (503) without leaving the step", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(inConsultantSelection());
    vi.mocked(api.chooseOffice).mockRejectedValue(new ApiError(503, "workflowUnavailable"));
    renderApp("/requests/12");

    await user().click(await screen.findByRole("button", { name: "التقدم في الطلب" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("خدمة سير العمل في مدن غير متاحة");
    expect(screen.getByRole("button", { name: "التقدم في الطلب" })).toBeEnabled();
  });

  it("shows progress, reports and the selected offices of an approved licence, in English", async () => {
    vi.mocked(api.getRequest).mockResolvedValue(
      inConsultantSelection({
        status: "approved",
        currentTask: null,
        building: building({
          selectedConsultant: consultantOffice,
          consultantQuotation: quotation({ state: "selected", commitmentsAccepted: true, phases: [{ phase: "sitePreparation", sharePercent: 4, durationDays: 30 }] }),
          currentProgress: [{ phase: "sitePreparation", completionPercent: 100 }, { phase: "foundations", completionPercent: 35.5 }],
          progressReports: [
            {
              id: 9, period: "2026-10", office: consultantOffice, submittedByUserId: "u4", submittedByEmail: "consultant@modon.local",
              submittedAt: "2026-10-18T12:00:00Z", comment: "On schedule", attachment: { fileName: "report.pdf", contentType: "application/pdf", sizeBytes: 2048 },
              phases: [{ phase: "sitePreparation", completionPercent: 100 }, { phase: "foundations", completionPercent: 35.5 }],
            },
          ],
        }),
      }),
    );
    renderApp("/requests/12", "en");

    expect(await screen.findByRole("heading", { name: "Monthly construction-progress reports" })).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Foundations" })).toHaveAttribute("aria-valuenow", "35.5");
    expect(screen.getByText("10/2026")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "report.pdf" })).toHaveAttribute("href", "/api/modon/requests/12/building/progress-reports/9/attachment");
    expect(screen.getByRole("heading", { name: "Selected consulting office" })).toBeInTheDocument();
    expect(screen.getByText("Commitments confirmed")).toBeInTheDocument();
    expect(document.documentElement.dir).toBe("ltr");
  });
});

describe("Consulting offices and contractors", () => {
  beforeEach(() => vi.mocked(api.getPersonas).mockResolvedValue(consultantPersona));

  it("lists open projects, invitations and awarded projects", async () => {
    vi.mocked(api.listProjects).mockResolvedValue([
      { requestId: 12, requestNumber: "BLR-000012-18-10-26", projectNumber: "Bid-00400-18-10-26", status: "consultantTender", stage: "consultant",
        relation: "openTender", licenceTypeCode: "new-building", investor: null, since: "2026-10-18T09:00:00Z", answered: false },
      { requestId: 13, requestNumber: "BLR-000013-18-10-26", projectNumber: null, status: "awaitingConsultantDecision", stage: "consultant",
        relation: "invited", licenceTypeCode: "new-building", investor: null, since: "2026-10-18T09:00:00Z", answered: false },
    ]);
    renderApp("/projects");

    const open = within((await screen.findByRole("heading", { name: "المشاريع المطروحة" })).closest("section")!);
    expect(open.getByText("Bid-00400-18-10-26")).toBeInTheDocument();
    expect(open.getByText("ترخيص بناء جديد")).toBeInTheDocument();
    const invited = within(screen.getByRole("heading", { name: "طلبات بانتظار ردك" }).closest("section")!);
    expect(invited.getByRole("link", { name: "فتح" })).toHaveAttribute("href", "/projects/13");
    expect(within(screen.getByRole("heading", { name: "المشاريع الراسية" }).closest("section")!).getByText("لا توجد مشاريع.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "المشاريع" })).toHaveAttribute("href", "/projects");
  });

  it("submits a quotation for an open project", async () => {
    vi.mocked(api.getProject).mockResolvedValue(project());
    vi.mocked(api.submitQuotation).mockResolvedValue(project({ canQuote: false, myQuotations: [quotation({ amountSar: 150_000 })] }));
    renderApp("/projects/12");

    await user().click(await screen.findByRole("button", { name: "تقديم" }));
    expect(screen.getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    await user().type(screen.getByRole("spinbutton", { name: "قيمة عرض السعر" }), "150000");
    await user().click(screen.getByRole("button", { name: "تقديم" }));

    expect(api.submitQuotation).toHaveBeenCalledWith(12, consultantOffice.id, 150000, null);
    expect(await screen.findByText(/تم تقديم عرض السعر/)).toBeInTheDocument();
    expect(screen.getByText("150,000 ريال")).toBeInTheDocument();
  });

  it("accepts an invitation with the phase plan and commitments", async () => {
    vi.mocked(api.getProject).mockResolvedValue(project({ relation: "invited", status: "awaitingConsultantDecision", canQuote: false, canRespond: true,
      currentRound: tender({ mode: "direct", number: null }) }));
    vi.mocked(api.respondToInvitation).mockResolvedValue(project({ relation: "awarded", status: "contractorSelection", canQuote: false }));
    renderApp("/projects/12");

    await user().click(await screen.findByRole("radio", { name: "قبول" }));
    await user().click(screen.getByRole("button", { name: "تقديم" }));
    expect(api.respondToInvitation).not.toHaveBeenCalled();
    expect(screen.getByRole("checkbox", { name: /تعهدات المكتب الاستشاري/ })).toHaveAttribute("aria-invalid", "true");

    const shares = [4, 4, 10, 25, 13, 60]; // the manual's example (adds up to 116 %)
    const names = ["تجهيز الموقع", "أعمال الأساسات", "الهياكل الإنشائية", "الحوائط والعزل والأرضيات", "الأعمال الكهربائية والميكانيكية والمعمارية", "تشطيب الموقع العام والسور"];
    for (const [i, name] of names.entries()) {
      await user().type(screen.getByRole("spinbutton", { name: `${name} – نسبة كل مرحلة في المشروع (%)` }), String(shares[i]));
      await user().type(screen.getByRole("spinbutton", { name: `${name} – مدة التنفيذ (يوم)` }), "30");
    }
    await user().click(screen.getByRole("checkbox", { name: /تعهدات المكتب الاستشاري/ }));
    await user().type(screen.getByRole("spinbutton", { name: "قيمة عرض السعر" }), "4000000");
    await user().click(screen.getByRole("button", { name: "تقديم" }));

    expect(api.respondToInvitation).toHaveBeenCalledWith(
      12,
      expect.objectContaining({ decision: "accept", amountSar: 4000000, commitmentsAccepted: true }),
      null,
    );
    const body = vi.mocked(api.respondToInvitation).mock.calls[0][1];
    expect(body.phases!.map((p) => p.sharePercent)).toEqual(shares);
    expect(await screen.findByText(/تم إرسال القبول/)).toBeInTheDocument();
  });

  it("rejects an invitation with a required comment and returns to the project list", async () => {
    vi.mocked(api.getProject).mockResolvedValue(project({ relation: "invited", canQuote: false, canRespond: true, currentRound: tender({ mode: "direct" }) }));
    vi.mocked(api.respondToInvitation).mockResolvedValue(null);
    vi.mocked(api.listProjects).mockResolvedValue([]);
    renderApp("/projects/12");

    await user().click(await screen.findByRole("radio", { name: "رفض" }));
    await user().click(screen.getByRole("button", { name: "تقديم" }));
    expect(screen.getByText("هذا الحقل مطلوب")).toBeInTheDocument();
    await user().type(screen.getByLabelText(/تعليق مقدم العرض/), "تم الرفض");
    await user().click(screen.getByRole("button", { name: "تقديم" }));

    expect(api.respondToInvitation).toHaveBeenCalledWith(12, expect.objectContaining({ decision: "reject", comment: "تم الرفض", phases: null }), null);
    expect(await screen.findByText("تم إرسال الرفض وإعادة الطلب إلى المستثمر.")).toBeInTheDocument();
  });

  it("submits a monthly report, checking each percentage is 0–100", async () => {
    vi.mocked(api.getProject).mockResolvedValue(project({ relation: "awarded", status: "approved", canQuote: false, canReport: true }));
    vi.mocked(api.submitProgressReport).mockResolvedValue(project({ relation: "awarded", status: "approved", canQuote: false, canReport: true }));
    renderApp("/projects/12");

    const site = await screen.findByRole("spinbutton", { name: "تجهيز الموقع – نسبة الإنجاز (%)" });
    await user().clear(site);
    await user().type(site, "120");
    await user().click(screen.getByRole("button", { name: "تقديم التقرير" }));
    expect(screen.getByText("يجب أن تكون النسبة بين 0 و 100")).toBeInTheDocument();
    expect(api.submitProgressReport).not.toHaveBeenCalled();

    await user().clear(site);
    await user().type(site, "100");
    await user().click(screen.getByRole("button", { name: "تقديم التقرير" }));

    const [id, body, file] = vi.mocked(api.submitProgressReport).mock.calls[0];
    expect(file).toBeNull();
    expect(id).toBe(12);
    expect(body.phases[0]).toEqual({ phase: "sitePreparation", completionPercent: 100 });
    expect(body.period).toMatch(/^\d{4}-\d{2}$/);
    expect(await screen.findByText("تم تقديم التقرير الشهري.")).toBeInTheDocument();
  });

  it("links an office's invitation task to the project page", async () => {
    const task: TaskItem = {
      requestId: 13, requestNumber: "BLR-000013-18-10-26", serviceCode: "building-licence", serviceNameAr: "رخصة البناء وتقارير البناء",
      serviceNameEn: "Building Licence & Construction Reports", status: "awaitingConsultantDecision", kind: "respondAsConsultant",
      instructions: null, assignedAt: "2026-10-18T09:00:00Z", submittedAt: "2026-10-18T09:00:00Z",
    };
    vi.mocked(api.listTasks).mockResolvedValue([task]);
    renderApp("/tasks");

    expect(await screen.findByRole("link", { name: "الرد على اختيار المستثمر" })).toHaveAttribute("href", "/projects/13");
    expect(screen.getByRole("link", { name: "BLR-000013-18-10-26" })).toHaveAttribute("href", "/projects/13");
  });
});
