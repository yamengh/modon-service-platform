using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FormCMS.Auth.Models;
using Modon.Platform.Identity;
using Modon.Platform.Requests;
using Modon.Platform.Review;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

[Collection("Modon API")]
public class ReviewTests(ModonAppFactory factory)
{
    private const string ReviewUrl = "/api/modon/review";
    private static string RequestUrl(long id) => $"{ReviewUrl}/requests/{id}";

    /// <summary>A new contractor with a fully valid, submitted excavation request.</summary>
    private async Task<(HttpClient Applicant, string Email, RequestDetail Request)> SubmittedRequest()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await factory.LoginAs(email);
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, draft.Id);
        return (client, email, await Read(await Submit(client, draft.Id)));
    }

    private Task<HttpClient> Reviewer() => factory.LoginAs("reviewer@modon.local");

    private static async Task<T> Body<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static Task<HttpResponseMessage> Decide(HttpClient client, long id, string action, object? body = null)
        => client.PostAsJsonAsync($"{RequestUrl(id)}/{action}", body ?? new { }, Json);

    private static async Task<string> UserId(HttpClient client)
        => (await client.GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.Id;

    // --- visibility and details ----------------------------------------------------------------

    [Fact]
    public async Task Reviewer_ListsSubmittedRequests_ButNeverDrafts()
    {
        var (applicant, _, submitted) = await SubmittedRequest();
        var draft = await CreateDraft(applicant);
        var reviewer = await Reviewer();

        var found = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?search={submitted.RequestNumber}"));
        var draftSearch = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?search={draft.RequestNumber}"));
        var all = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?pageSize=100"));

        var item = Assert.Single(found.Items);
        Assert.Equal((submitted.Id, RequestStatus.InProgress, "riyadh-2"), (item.Id, item.Status, item.IndustrialCityCode));
        Assert.Equal("Excavation Works Permit", item.ServiceNameEn);
        Assert.NotNull(item.SubmittedAt);
        Assert.Empty(draftSearch.Items);
        Assert.DoesNotContain(all.Items, i => i.Status == RequestStatus.Draft);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync(RequestUrl(draft.Id))).StatusCode);
    }

    [Fact]
    public async Task Reviewer_SeesFullDetails_ApplicantAttachmentsAndHistory()
    {
        var (_, email, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();

        var detail = await Body<ReviewRequestDetail>(await reviewer.GetAsync(RequestUrl(submitted.Id)));

        Assert.Equal(email, detail.Applicant.Email);
        Assert.Equal("Test establishment", detail.Establishment!.NameEn);
        Assert.Equal("open-trench", detail.Excavation.ExcavationMethodCode);
        Assert.Equal(new DateOnly(2026, 11, 30), detail.Excavation.EndDate);
        Assert.Equal(ExcavationSlots, detail.Attachments.Select(a => a.SlotCode));
        Assert.Equal([RequestAction.Approve, RequestAction.ReturnForCompletion, RequestAction.Reject], detail.AvailableDecisions);
        Assert.Collection(detail.History,
            h => Assert.Equal((RequestAction.Create, email, true), (h.Action, h.ActorEmail, h.ActorIsApplicant)),
            h => Assert.Equal((RequestAction.Submit, email, true), (h.Action, h.ActorEmail, h.ActorIsApplicant)));
    }

    [Fact]
    public async Task Queue_FiltersByStatusAndService_AndValidatesTheStatusFilter()
    {
        var (_, _, toApprove) = await SubmittedRequest();
        var (_, _, stillOpen) = await SubmittedRequest();
        var reviewer = await Reviewer();
        await Body<ReviewRequestDetail>(await Decide(reviewer, toApprove.Id, "approve"));

        var approved = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?status=approved&pageSize=100"));
        var inProgress = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?status=inProgress&service=excavation-permit&pageSize=100"));
        var buildingLicences = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?service=building-licence"));
        var paged = await Body<ReviewQueuePage>(await reviewer.GetAsync($"{ReviewUrl}/requests?pageSize=1&page=2"));

        Assert.All(approved.Items, i => Assert.Equal(RequestStatus.Approved, i.Status));
        Assert.Contains(approved.Items, i => i.Id == toApprove.Id);
        Assert.DoesNotContain(approved.Items, i => i.Id == stillOpen.Id);
        Assert.Contains(inProgress.Items, i => i.Id == stillOpen.Id);
        Assert.All(inProgress.Items, i => Assert.Equal(("excavation-permit", RequestStatus.InProgress), (i.ServiceCode, i.Status)));
        Assert.Equal(0, buildingLicences.Total);
        Assert.Equal((1, 2), (paged.Items.Length, paged.Page));
        Assert.True(paged.Total >= 2);

        foreach (var bad in new[] { "draft", "1", "nonsense" })
        {
            var errors = await Errors(await reviewer.GetAsync($"{ReviewUrl}/requests?status={bad}"));
            Assert.Equal([ErrorCodes.UnknownOption], errors["status"]);
        }
    }

    [Fact]
    public async Task Summary_CountsRequestsByStatus_WithoutDrafts()
    {
        var reviewer = await Reviewer();
        var before = await Body<ReviewSummary>(await reviewer.GetAsync($"{ReviewUrl}/summary"));

        var (applicant, _, submitted) = await SubmittedRequest();
        await CreateDraft(applicant);
        var afterSubmit = await Body<ReviewSummary>(await reviewer.GetAsync($"{ReviewUrl}/summary"));
        await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "reject", new { reason = "incomplete plans" }));
        var afterReject = await Body<ReviewSummary>(await reviewer.GetAsync($"{ReviewUrl}/summary"));

        Assert.DoesNotContain("draft", before.ByStatus.Keys);
        Assert.Equal(before.Total + 1, afterSubmit.Total);
        Assert.Equal(before.ByStatus["inProgress"] + 1, afterSubmit.ByStatus["inProgress"]);
        Assert.Equal(afterSubmit.ByStatus["inProgress"] - 1, afterReject.ByStatus["inProgress"]);
        Assert.Equal(afterSubmit.ByStatus["rejected"] + 1, afterReject.ByStatus["rejected"]);
        Assert.Equal(afterSubmit.Total, afterReject.Total);
    }

    // --- authorization -------------------------------------------------------------------------

    [Fact]
    public async Task NonReviewers_AreForbidden_AndAnonymousIsUnauthorized()
    {
        var (applicant, _, submitted) = await SubmittedRequest();
        var (adminEmail, _) = await factory.CreateUserAsync([Roles.Admin]);
        var callers = new[]
        {
            applicant, // the request's own contractor
            await factory.LoginAs("investor@modon.local"),
            await factory.LoginAs("contractor@modon.local"),
            await factory.LoginAs(adminEmail), // FormCMS "admin" is not a Modon reviewer
        };

        foreach (var client in callers)
        {
            foreach (var res in await CallEveryReviewEndpoint(client, submitted.Id))
            {
                Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            }
        }
        foreach (var res in await CallEveryReviewEndpoint(factory.NewClient(), submitted.Id))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        var unchanged = await Read(await applicant.GetAsync($"{RequestsUrl}/{submitted.Id}"));
        Assert.Equal(RequestStatus.InProgress, unchanged.Status);
        Assert.Equal(2, unchanged.History.Length);
    }

    private static async Task<HttpResponseMessage[]> CallEveryReviewEndpoint(HttpClient client, long id) =>
    [
        await client.GetAsync($"{ReviewUrl}/summary"),
        await client.GetAsync($"{ReviewUrl}/requests"),
        await client.GetAsync(RequestUrl(id)),
        await client.GetAsync($"{RequestUrl(id)}/history"),
        await client.GetAsync($"{RequestUrl(id)}/attachments/workRoutePlan"),
        await Decide(client, id, "approve"),
        await Decide(client, id, "reject", new { reason = "x" }),
        await Decide(client, id, "return", new { comment = "x" }),
    ];

    [Fact]
    public async Task SuperAdmin_CanReview_AndPersonasReportReviewAccess()
    {
        var sa = await factory.LoginAs("sadmin@modon.local");

        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync($"{ReviewUrl}/requests")).StatusCode);
        Assert.True((await sa.GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.CanReview);
        Assert.True((await (await Reviewer()).GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.CanReview);
        Assert.False((await (await factory.LoginAs("contractor@modon.local"))
            .GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.CanReview);
    }

    [Fact]
    public async Task DecisionCalls_WithoutClientHeader_AreRejected()
    {
        var (_, _, submitted) = await SubmittedRequest();
        var client = factory.NewClient(withClientHeader: false);
        (await client.PostAsJsonAsync("/api/login",
            new { usernameOrEmail = "reviewer@modon.local", password = ModonAppFactory.Password })).EnsureSuccessStatusCode();

        var res = await Decide(client, submitted.Id, "approve");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ProblemCodes.MissingClientHeader, await ProblemTitle(res));
    }

    // --- decisions -----------------------------------------------------------------------------

    [Fact]
    public async Task Approve_RecordsReviewerActionAndTime_InExactlyOneHistoryRow()
    {
        var (applicant, _, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();
        var reviewerId = await UserId(reviewer);

        var approved = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "approve", new { comment = "  all good  " }));

        Assert.Equal(RequestStatus.Approved, approved.Status);
        Assert.Empty(approved.AvailableDecisions);
        Assert.Equal(3, approved.History.Length);
        var last = approved.History[^1];
        Assert.Equal((RequestStatus.InProgress, RequestStatus.Approved, RequestAction.Approve), (last.FromStatus, last.ToStatus, last.Action));
        Assert.Equal((reviewerId, "reviewer@modon.local", false), (last.ActorUserId, last.ActorEmail, last.ActorIsApplicant));
        Assert.Equal("all good", last.Comment);
        Assert.True(last.OccurredAt >= submitted.SubmittedAt);

        var audit = await Body<ReviewHistoryItem[]>(await reviewer.GetAsync($"{RequestUrl(submitted.Id)}/history"));
        Assert.Equal(approved.History, audit);

        // The applicant sees the outcome in their own view, and can no longer change the request.
        var mine = await Read(await applicant.GetAsync($"{RequestsUrl}/{submitted.Id}"));
        Assert.Equal(RequestStatus.Approved, mine.Status);
        Assert.Equal(RequestAction.Approve, mine.History[^1].Action);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveExcavation(applicant, submitted.Id, new { })).StatusCode);
    }

    [Fact]
    public async Task Reject_RequiresAReason()
    {
        var (_, _, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();

        foreach (var body in new object[] { new { }, new { reason = "" }, new { reason = "   " }, new { comment = "wrong field" } })
        {
            var errors = await Errors(await Decide(reviewer, submitted.Id, "reject", body));
            Assert.Equal([ErrorCodes.Required], errors["reason"]);
        }
        var tooLong = await Errors(await Decide(reviewer, submitted.Id, "reject", new { reason = new string('x', ReviewService.MaxCommentLength + 1) }));
        Assert.Equal([ErrorCodes.TooLong], tooLong["reason"]);

        var unchanged = await Body<ReviewRequestDetail>(await reviewer.GetAsync(RequestUrl(submitted.Id)));
        Assert.Equal(RequestStatus.InProgress, unchanged.Status);
        Assert.Equal(2, unchanged.History.Length);

        var rejected = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "reject", new { reason = "مخطط المسار غير واضح" }));
        Assert.Equal(RequestStatus.Rejected, rejected.Status);
        Assert.Equal((RequestAction.Reject, "مخطط المسار غير واضح"), (rejected.History[^1].Action, rejected.History[^1].Comment));
        Assert.Equal(3, rejected.History.Length);
    }

    [Fact]
    public async Task Return_RequiresAComment()
    {
        var (_, _, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();

        foreach (var body in new object[] { new { }, new { comment = " " }, new { reason = "wrong field" } })
        {
            var errors = await Errors(await Decide(reviewer, submitted.Id, "return", body));
            Assert.Equal([ErrorCodes.Required], errors["comment"]);
        }
        Assert.Equal(2, (await Body<ReviewRequestDetail>(await reviewer.GetAsync(RequestUrl(submitted.Id)))).History.Length);

        var returned = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "return", new { comment = "أرفق خطاب الجهة المالكة موقعاً" }));
        Assert.Equal(RequestStatus.ReturnedForCompletion, returned.Status);
        var last = returned.History[^1];
        Assert.Equal((RequestStatus.InProgress, RequestStatus.ReturnedForCompletion, RequestAction.ReturnForCompletion),
            (last.FromStatus, last.ToStatus, last.Action));
        Assert.Equal("أرفق خطاب الجهة المالكة موقعاً", last.Comment);
    }

    [Fact]
    public async Task InvalidTransitions_AreRejected_WithoutHistory()
    {
        var reviewer = await Reviewer();
        var (_, _, approved) = await SubmittedRequest();
        var (_, _, rejected) = await SubmittedRequest();
        var (_, _, returned) = await SubmittedRequest();
        await Body<ReviewRequestDetail>(await Decide(reviewer, approved.Id, "approve"));
        await Body<ReviewRequestDetail>(await Decide(reviewer, rejected.Id, "reject", new { reason = "r" }));
        await Body<ReviewRequestDetail>(await Decide(reviewer, returned.Id, "return", new { comment = "c" }));

        var attempts = new (long Id, string Action, object Body)[]
        {
            (approved.Id, "approve", new { }),
            (approved.Id, "reject", new { reason = "late" }),
            (rejected.Id, "approve", new { }),
            (rejected.Id, "return", new { comment = "c" }),
            (returned.Id, "approve", new { }),
            (returned.Id, "reject", new { reason = "r" }),
        };
        foreach (var (id, action, body) in attempts)
        {
            var res = await Decide(reviewer, id, action, body);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal(ProblemCodes.InvalidTransition, await ProblemTitle(res));
        }
        foreach (var id in new[] { approved.Id, rejected.Id, returned.Id })
        {
            Assert.Equal(3, (await Body<ReviewRequestDetail>(await reviewer.GetAsync(RequestUrl(id)))).History.Length);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Decide(reviewer, long.MaxValue, "approve")).StatusCode);
    }

    [Fact]
    public async Task DecisionsOnDrafts_AreNotPossible()
    {
        var (email, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var draft = await CreateDraft(await factory.LoginAs(email));
        var reviewer = await Reviewer();

        Assert.Equal(HttpStatusCode.NotFound, (await Decide(reviewer, draft.Id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(reviewer, draft.Id, "reject", new { reason = "r" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(reviewer, draft.Id, "return", new { comment = "c" })).StatusCode);
    }

    [Fact]
    public async Task SimultaneousDecisions_ProduceExactlyOneTransition()
    {
        var (_, _, submitted) = await SubmittedRequest();
        var first = await Reviewer();
        var second = await Reviewer();

        var results = await Task.WhenAll(
            Decide(first, submitted.Id, "approve"),
            Decide(second, submitted.Id, "reject", new { reason = "conflict" }));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var detail = await Body<ReviewRequestDetail>(await first.GetAsync(RequestUrl(submitted.Id)));
        Assert.Equal(3, detail.History.Length);
        Assert.Contains(detail.Status, new[] { RequestStatus.Approved, RequestStatus.Rejected });
    }

    // --- attachments ---------------------------------------------------------------------------

    [Fact]
    public async Task Reviewer_CanDownloadSubmittedFiles_WhileOtherApplicantsCannot()
    {
        var (applicant, _, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();
        var (otherEmail, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var other = await factory.LoginAs(otherEmail);

        var download = await reviewer.GetAsync($"{RequestUrl(submitted.Id)}/attachments/workRoutePlan");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(HttpStatusCode.NotFound,
            (await reviewer.GetAsync($"{RequestUrl(submitted.Id)}/attachments/coordinationLetter")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"{RequestsUrl}/{submitted.Id}/attachments/workRoutePlan")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync($"{RequestUrl(submitted.Id)}/attachments/workRoutePlan")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await applicant.GetAsync($"{RequestsUrl}/{submitted.Id}/attachments/workRoutePlan")).StatusCode);
    }

    [Fact]
    public async Task ReviewerCannotDownloadDraftFiles()
    {
        var (email, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var applicant = await factory.LoginAs(email);
        var draft = await CreateDraft(applicant);
        (await Upload(applicant, draft.Id, "workRoutePlan", Pdf, "route.pdf", "application/pdf")).EnsureSuccessStatusCode();

        var res = await (await Reviewer()).GetAsync($"{RequestUrl(draft.Id)}/attachments/workRoutePlan");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task ReviewSummaryJson_UsesCamelCaseStatusKeys()
    {
        var res = await (await Reviewer()).GetAsync($"{ReviewUrl}/summary");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();

        var keys = json.GetProperty("byStatus").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["inProgress", "returnedForCompletion", "approved", "rejected", "cancelled"], keys);
    }
}
