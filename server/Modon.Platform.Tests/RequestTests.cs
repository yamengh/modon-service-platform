using System.Net;
using System.Net.Http.Json;
using Modon.Platform.Requests;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

[Collection("Modon API")]
public class RequestTests(ModonAppFactory factory)
{
    private async Task<(HttpClient Client, long EstablishmentId)> NewContractor()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        return (await factory.LoginAs(email), establishmentId);
    }

    [Fact]
    public async Task Contractor_CreatesDraft_WithNumberStatusAndHistory()
    {
        var (client, _) = await NewContractor();

        var draft = await CreateDraft(client);

        Assert.Equal(RequestStatus.Draft, draft.Status);
        Assert.Matches(@"^EXC-\d{6}-\d{2}-\d{2}-\d{2}$", draft.RequestNumber);
        Assert.Equal("تصريح أعمال الحفر", draft.ServiceNameAr);
        Assert.True(draft.CanEdit);
        Assert.Equal(6, draft.AttachmentSlots.Length);
        var created = Assert.Single(draft.History);
        Assert.Equal((null, RequestStatus.Draft, RequestAction.Create), (created.FromStatus, created.ToStatus, created.Action));
    }

    [Fact]
    public async Task Investor_CannotStartAnExcavationPermit()
    {
        var client = await factory.LoginAs("investor@modon.local");

        var res = await client.PostAsJsonAsync(RequestsUrl, new { serviceCode = "excavation-permit" });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(ProblemCodes.RoleNotAllowed, await ProblemTitle(res));
    }

    [Fact]
    public async Task Anonymous_CannotUseRequests()
    {
        var client = factory.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(RequestsUrl, new { serviceCode = "excavation-permit" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(RequestsUrl)).StatusCode);
    }

    [Fact]
    public async Task BuildingLicence_AcceptsApplicationsFromInvestorsOnly()
    {
        // Milestone 6 opened the building licence (it was refused with serviceNotOpen before).
        var investor = await factory.LoginAs("investor@modon.local");
        var contractor = await factory.LoginAs("contractor@modon.local");

        var created = await investor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "building-licence" });
        var refused = await contractor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "building-licence" });
        var unknown = await investor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "no-such-service" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(ProblemCodes.RoleNotAllowed, await ProblemTitle(refused));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(ProblemCodes.ServiceNotOpen, await ProblemTitle(unknown));
    }

    [Fact]
    public async Task StateChangingCalls_WithoutClientHeader_AreRejected()
    {
        var (email, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = factory.NewClient(withClientHeader: false);
        (await client.PostAsJsonAsync("/api/login", new { usernameOrEmail = email, password = ModonAppFactory.Password }))
            .EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync(RequestsUrl, new { serviceCode = "excavation-permit" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ProblemCodes.MissingClientHeader, await ProblemTitle(res));
    }

    [Fact]
    public async Task PartialDraft_CanBeSaved_AndReopened()
    {
        var (client, establishmentId) = await NewContractor();
        var draft = await CreateDraft(client);

        var saved = await Read(await SaveExcavation(client, draft.Id, new
        {
            establishmentId,
            industrialCityCode = "dammam-2",
            workDescription = "  حفر تجريبي  ",
            issueDate = "2026-12-01",
            lengthM = 15.5,
        }));
        var reopened = await Read(await client.GetAsync($"{RequestsUrl}/{draft.Id}"));

        Assert.Equal(RequestStatus.Draft, saved.Status);
        Assert.Equal(establishmentId, reopened.Excavation.EstablishmentId);
        Assert.Equal("dammam-2", reopened.Excavation.IndustrialCityCode);
        Assert.Equal("حفر تجريبي", reopened.Excavation.WorkDescription);
        Assert.Equal(new DateOnly(2026, 12, 1), reopened.Excavation.IssueDate);
        Assert.Equal(15.5m, reopened.Excavation.LengthM);
        Assert.Null(reopened.Excavation.EndDate);
    }

    [Fact]
    public async Task Draft_RejectsInvalidValues_EvenWhenIncomplete()
    {
        var (client, _) = await NewContractor();
        var draft = await CreateDraft(client);

        var errors = await Errors(await SaveExcavation(client, draft.Id, new
        {
            issueDate = "2026-12-10",
            endDate = "2026-12-10",
            requestTypeCode = "not-a-real-option",
            projectManagerEmail = "not-an-email",
            projectManagerPhone = "abc",
            lengthM = -3,
            workDescription = new string('x', ExcavationLimits.WorkDescription + 1),
        }));

        Assert.Equal([ErrorCodes.EndDateMustBeAfterIssueDate], errors["endDate"]);
        Assert.Equal([ErrorCodes.UnknownOption], errors["requestTypeCode"]);
        Assert.Equal([ErrorCodes.InvalidEmail], errors["projectManagerEmail"]);
        Assert.Equal([ErrorCodes.InvalidPhone], errors["projectManagerPhone"]);
        Assert.Equal([ErrorCodes.MustBePositive], errors["lengthM"]);
        Assert.Equal([ErrorCodes.TooLong], errors["workDescription"]);
        Assert.DoesNotContain("issueDate", errors.Keys); // drafts don't require fields

        var reopened = await Read(await client.GetAsync($"{RequestsUrl}/{draft.Id}"));
        Assert.Null(reopened.Excavation.IssueDate); // nothing was saved
    }

    [Fact]
    public async Task Draft_RejectsAnotherUsersEstablishment()
    {
        var (client, _) = await NewContractor();
        var (_, otherEstablishment) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var draft = await CreateDraft(client);

        var errors = await Errors(await SaveExcavation(client, draft.Id, new { establishmentId = otherEstablishment }));

        Assert.Equal([ErrorCodes.NotYourEstablishment], errors["establishmentId"]);
    }

    [Fact]
    public async Task OtherUsers_CannotSeeOrChangeARequest()
    {
        var (owner, ownerEstablishment) = await NewContractor();
        var draft = await CreateDraft(owner);
        var (intruder, _) = await NewContractor();
        var url = $"{RequestsUrl}/{draft.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SaveExcavation(intruder, draft.Id, new { establishmentId = ownerEstablishment })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Submit(intruder, draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Upload(intruder, draft.Id, "workRoutePlan", Pdf, "x.pdf", "application/pdf")).StatusCode);
        var intruderList = await intruder.GetFromJsonAsync<RequestSummary[]>(RequestsUrl, Json);
        Assert.DoesNotContain(intruderList!, r => r.Id == draft.Id);
    }

    [Fact]
    public async Task Submit_WithMissingFields_ListsEveryRequiredField_AndStaysDraft()
    {
        var (client, _) = await NewContractor();
        var draft = await CreateDraft(client);

        var errors = await Errors(await Submit(client, draft.Id));

        string[] required =
        [
            "establishmentId", "requestTypeCode", "contractTypeCode", "industrialCityCode", "permitTypeCode",
            "workDescription", "issueDate", "endDate", "excavationMethodCode", "workLocation",
            "projectManagerName", "projectManagerPhone", "projectManagerEmail", "supervisingEntity",
        ];
        foreach (var field in required) Assert.Equal([ErrorCodes.Required], errors[field]);
        foreach (var dimension in new[] { "lengthM", "widthM", "depthM", "totalPathLengthM" })
            Assert.DoesNotContain(dimension, errors.Keys);

        var reopened = await Read(await client.GetAsync($"{RequestsUrl}/{draft.Id}"));
        Assert.Equal(RequestStatus.Draft, reopened.Status);
        Assert.Single(reopened.History);
    }

    [Fact]
    public async Task Submit_WithCompleteFormButMissingAttachments_ListsMissingSlots()
    {
        var (client, establishmentId) = await NewContractor();
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        (await Upload(client, draft.Id, "workRoutePlan", Pdf, "route.pdf", "application/pdf")).EnsureSuccessStatusCode();

        var errors = await Errors(await Submit(client, draft.Id));

        Assert.Equal(
            ExcavationSlots.Where(s => s != "workRoutePlan").Select(s => $"attachments.{s}").Order(),
            errors.Keys.Order());
    }

    [Fact]
    public async Task Submit_ValidRequest_MovesToInProgress_AndLocksIt()
    {
        var (client, establishmentId) = await NewContractor();
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, draft.Id);

        var submitted = await Read(await Submit(client, draft.Id));

        Assert.Equal(RequestStatus.InProgress, submitted.Status);
        Assert.False(submitted.CanEdit);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Equal(6, submitted.Attachments.Length);
        Assert.Collection(submitted.History,
            h => Assert.Equal((null, RequestStatus.Draft, RequestAction.Create), (h.FromStatus, h.ToStatus, h.Action)),
            h => Assert.Equal((RequestStatus.Draft, RequestStatus.InProgress, RequestAction.Submit),
                (h.FromStatus, h.ToStatus, h.Action)));

        // Once submitted, the applicant can no longer change it.
        Assert.Equal(HttpStatusCode.Conflict, (await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(client, draft.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Upload(client, draft.Id, "workRoutePlan", Pdf, "new.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan")).StatusCode);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyOwnRequests_NewestFirst_WithServiceNames()
    {
        var (client, establishmentId) = await NewContractor();
        var first = await CreateDraft(client);
        await Read(await SaveExcavation(client, first.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, first.Id);
        await Read(await Submit(client, first.Id));
        var second = await CreateDraft(client);

        var list = (await client.GetFromJsonAsync<RequestSummary[]>(RequestsUrl, Json))!;

        Assert.Equal([second.Id, first.Id], list.Select(r => r.Id));
        Assert.Equal([RequestStatus.Draft, RequestStatus.InProgress], list.Select(r => r.Status));
        Assert.All(list, r => Assert.Equal("Excavation Works Permit", r.ServiceNameEn));
        Assert.Equal(first.RequestNumber, list[1].RequestNumber);
        Assert.NotNull(list[1].SubmittedAt);
        Assert.Null(list[0].SubmittedAt);
    }

    [Fact]
    public async Task MyEstablishments_ReturnsOnlyLinkedEstablishments()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await factory.LoginAs(email);

        var list = await client.GetFromJsonAsync<EstablishmentInfo[]>("/api/modon/me/establishments", Json);

        Assert.Equal([establishmentId], list!.Select(e => e.Id));
    }
}
