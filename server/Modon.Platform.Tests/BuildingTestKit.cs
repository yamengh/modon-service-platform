using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;
using Modon.Platform.Seeding;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>Users, forms and calls for building-licence tests (shared by the in-process and Camunda suites).</summary>
public sealed class BuildingTestKit(ModonAppFactory factory)
{
    public const string ServiceCode = "building-licence";
    public const string ProjectsUrl = "/api/modon/building/projects";

    public sealed record Actor(HttpClient Client, string Email, long EstablishmentId);

    public async Task<Actor> Investor()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Investor]);
        return new Actor(await factory.LoginAs(email), email, establishmentId);
    }

    public async Task<Actor> Consultant()
    {
        var (email, officeId) = await factory.CreateUserAsync([ModonRoles.Consultant], DevUserSeeder.OfficeKind.ConsultingOffice);
        return new Actor(await factory.LoginAs(email), email, officeId);
    }

    public async Task<Actor> Contractor()
    {
        var (email, officeId) = await factory.CreateUserAsync([ModonRoles.Contractor], DevUserSeeder.OfficeKind.Contractor);
        return new Actor(await factory.LoginAs(email), email, officeId);
    }

    public Task<HttpClient> Reviewer() => factory.LoginAs("reviewer@modon.local");

    public static BuildingLicenceDto ValidForm(long establishmentId) => new(
        EstablishmentId: establishmentId,
        ContractNumber: "25000168",
        LicenceTypeCode: "new-building",
        ConstructionBudgetSar: 10_000_000m,
        FloorCount: 11,
        ScopeOfWork: "نطاق عمل التقديم على رخصة بناء جديدة",
        Notes: "لا يوجد",
        Contacts: [new BuildingContactDto("Sameh", "sameh@example.com", "01201001203")],
        TermsAccepted: true);

    public static async Task<RequestDetail> CreateBuildingDraft(HttpClient investor)
    {
        var res = await investor.PostAsJsonAsync(RequestsUrl, new { serviceCode = ServiceCode });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<RequestDetail>(Json))!;
    }

    public static Task<HttpResponseMessage> Save(HttpClient investor, long id, object body)
        => investor.PutAsJsonAsync($"{RequestsUrl}/{id}/building", body, Json);

    public static async Task UploadPlans(HttpClient investor, long id)
    {
        foreach (var slot in new[] { "approvedGeneralPlans", "approvedSafetyPlans" })
        {
            var res = await Upload(investor, id, slot, Pdf, $"{slot}.pdf", "application/pdf");
            Assert.True(res.IsSuccessStatusCode, $"{slot}: {await res.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>A complete draft (form + plans), not yet submitted.</summary>
    public async Task<(Actor Investor, RequestDetail Draft)> ReadyDraft()
    {
        var investor = await Investor();
        var draft = await CreateBuildingDraft(investor.Client);
        await Read(await Save(investor.Client, draft.Id, ValidForm(investor.EstablishmentId)));
        await UploadPlans(investor.Client, draft.Id);
        return (investor, draft);
    }

    /// <summary>Submitted basic information: the request waits in consultant selection.</summary>
    public async Task<(Actor Investor, RequestDetail Request)> Submitted()
    {
        var (investor, draft) = await ReadyDraft();
        return (investor, await Read(await Submit(investor.Client, draft.Id)));
    }

    public static Task<HttpResponseMessage> Choose(HttpClient investor, long id, string stage, SelectionMode mode, long? officeId = null)
        => investor.PostAsJsonAsync($"{RequestsUrl}/{id}/building/{stage}/selection", new SelectionBody(mode, officeId), Json);

    public static Task<HttpResponseMessage> AcceptQuotation(HttpClient investor, long id, string stage, long quotationId)
        => investor.PostAsync($"{RequestsUrl}/{id}/building/{stage}/quotations/{quotationId}/accept", null);

    public static Task<HttpResponseMessage> RejectQuotations(HttpClient investor, long id, string stage, string? comment = null)
        => investor.PostAsJsonAsync($"{RequestsUrl}/{id}/building/{stage}/quotations/reject-all", new InvestorCommentBody(comment), Json);

    public static Task<HttpResponseMessage> Quote(HttpClient office, long id, long officeId, decimal amount, string? comment = "عرضنا")
        => office.PostAsJsonAsync($"{ProjectsUrl}/{id}/quotations", new QuotationBody(officeId, amount, comment), Json);

    public static PhasePlanDto[] FullPlan() =>
    [
        new(ConstructionPhase.SitePreparation, 4, 30),
        new(ConstructionPhase.Foundations, 4, 10),
        new(ConstructionPhase.StructuralWorks, 10, 50),
        new(ConstructionPhase.WallsInsulationFloors, 25, 25),
        new(ConstructionPhase.ElectricalMechanicalArchitectural, 13, 13),
        new(ConstructionPhase.SiteFinishingAndFence, 60, 23), // the manual's example adds up to 116 %
    ];

    public static OfficeResponseBody ConsultantAccepts(decimal amount = 4_000_000m) =>
        new(OfficeDecision.Accept, amount, "نقبل", CommitmentsAccepted: true, Phases: FullPlan());

    public static Task<HttpResponseMessage> Respond(HttpClient office, long id, OfficeResponseBody body)
        => office.PostAsJsonAsync($"{ProjectsUrl}/{id}/response", body, Json);

    /// <summary>Multipart answer: "data" = JSON, "file" = the attachment.</summary>
    public static Task<HttpResponseMessage> RespondWithFile(HttpClient office, long id, OfficeResponseBody body,
        byte[] file, string fileName, string contentType)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(body, Json)), "data" },
            { content, "file", fileName },
        };
        return office.PostAsync($"{ProjectsUrl}/{id}/response", form);
    }

    public static PhaseProgressDto[] Progress(params decimal[] percents)
        => BuildingLicence.Phases.Select((p, i) => new PhaseProgressDto(p.Phase, percents[i])).ToArray();

    public static Task<HttpResponseMessage> Report(HttpClient consultant, long id, string period, PhaseProgressDto[] phases, string? comment = "تقرير شهري")
        => consultant.PostAsJsonAsync($"{ProjectsUrl}/{id}/progress-reports", new ProgressReportBody(period, phases, comment), Json);

    public static async Task<PartnerProjectDetail> Project(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<PartnerProjectDetail>(Json))!;
    }

    public static async Task<PartnerProjectDetail> GetProject(HttpClient office, long id)
        => await Project(await office.GetAsync($"{ProjectsUrl}/{id}"));

    public static async Task<RequestDetail> Get(HttpClient investor, long id)
        => await Read(await investor.GetAsync($"{RequestsUrl}/{id}"));

    public static async Task AssertProblem(HttpResponseMessage res, HttpStatusCode status, string title)
    {
        Assert.True(status == res.StatusCode, $"expected {status}, got {res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        Assert.Equal(title, await ProblemTitle(res));
    }

    // --- whole stages ----------------------------------------------------------------------------

    /// <summary>The investor offers the project to all consulting offices; one office quotes; the investor picks it.</summary>
    public async Task<Actor> ConsultantChosenByTender(Actor investor, long id)
    {
        var consultant = await Consultant();
        await Read(await Choose(investor.Client, id, "consultant", SelectionMode.OpenTender));
        var quoted = await Project(await Quote(consultant.Client, id, consultant.EstablishmentId, 200_000m));
        await Read(await AcceptQuotation(investor.Client, id, "consultant", quoted.MyQuotations.Single().Id));
        return consultant;
    }

    /// <summary>The investor chooses a contractor directly; it accepts. The request goes to Modon.</summary>
    public async Task<Actor> ContractorAcceptsDirectly(Actor investor, long id)
    {
        var contractor = await Contractor();
        await Read(await Choose(investor.Client, id, "contractor", SelectionMode.Direct, contractor.EstablishmentId));
        await Project(await Respond(contractor.Client, id, new OfficeResponseBody(OfficeDecision.Accept, 2_000_000m, null)));
        return contractor;
    }

    /// <summary>Submitted, consultant and contractor chosen: waiting for the Modon reviewer.</summary>
    public async Task<(Actor Investor, Actor Consultant, Actor Contractor, RequestDetail Request)> UnderReview()
    {
        var (investor, submitted) = await Submitted();
        var consultant = await ConsultantChosenByTender(investor, submitted.Id);
        var contractor = await ContractorAcceptsDirectly(investor, submitted.Id);
        return (investor, consultant, contractor, await Get(investor.Client, submitted.Id));
    }

    public static Task<HttpResponseMessage> Decide(HttpClient reviewer, long id, string action, object? body = null)
        => reviewer.PostAsJsonAsync($"/api/modon/review/requests/{id}/{action}", body ?? new { comment = "c" }, Json);
}
