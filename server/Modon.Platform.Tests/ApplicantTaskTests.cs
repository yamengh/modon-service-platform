using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Identity;
using Modon.Platform.Requests;
using Modon.Platform.Review;
using Modon.Platform.Seeding;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>
/// Milestone 4: returned requests (edit → resubmit), coordination letters, and the applicant's task inbox.
/// </summary>
[Collection("Modon API")]
public class ApplicantTaskTests(ModonAppFactory factory)
{
    private const string TasksUrl = "/api/modon/tasks";
    private static string ReviewUrl(long id) => $"/api/modon/review/requests/{id}";
    private static string RequestUrl(long id) => $"{RequestsUrl}/{id}";

    private static readonly string[] LetterSlots =
        ServiceApplications.ExcavationPermit.CoordinationSlots.Select(s => s.Code).ToArray();

    private sealed record Applicant(HttpClient Client, string Email, long EstablishmentId);

    private async Task<Applicant> NewContractor()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        return new Applicant(await factory.LoginAs(email), email, establishmentId);
    }

    /// <summary>A new contractor with a fully valid, submitted excavation request.</summary>
    private async Task<(Applicant Applicant, RequestDetail Request)> SubmittedRequest()
    {
        var applicant = await NewContractor();
        var draft = await CreateDraft(applicant.Client);
        await Read(await SaveExcavation(applicant.Client, draft.Id, ValidExcavation(applicant.EstablishmentId)));
        await UploadAllRequired(applicant.Client, draft.Id);
        return (applicant, await Read(await Submit(applicant.Client, draft.Id)));
    }

    private Task<HttpClient> Reviewer() => factory.LoginAs("reviewer@modon.local");

    private static Task<HttpResponseMessage> Decide(HttpClient reviewer, long id, string action, object? body = null)
        => reviewer.PostAsJsonAsync($"{ReviewUrl(id)}/{action}", body ?? new { }, Json);

    private static async Task<T> Body<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<(Applicant Applicant, RequestDetail Request)> ReturnedRequest(string comment = "أرفق خطاب الجهة المالكة موقعاً")
    {
        var (applicant, submitted) = await SubmittedRequest();
        await Body<ReviewRequestDetail>(await Decide(await Reviewer(), submitted.Id, "return", new { comment }));
        return (applicant, await Read(await applicant.Client.GetAsync(RequestUrl(submitted.Id))));
    }

    private async Task<(Applicant Applicant, RequestDetail Request)> AwaitingLetters(string? comment = "أرفق خطابات التنسيق الثلاثة")
    {
        var (applicant, submitted) = await SubmittedRequest();
        await Body<ReviewRequestDetail>(await Decide(await Reviewer(), submitted.Id, "request-coordination-letters", new { comment }));
        return (applicant, await Read(await applicant.Client.GetAsync(RequestUrl(submitted.Id))));
    }

    private static Task<HttpResponseMessage> SubmitLetters(HttpClient client, long id)
        => client.PostAsync($"{RequestUrl(id)}/coordination-letters/submit", null);

    private static async Task UploadAllLetters(HttpClient client, long id)
    {
        foreach (var slot in LetterSlots)
        {
            var res = slot == LetterSlots[0]
                ? await Upload(client, id, slot, Png, "letter.png", "image/png")
                : await Upload(client, id, slot, Pdf, $"{slot}.pdf", "application/pdf");
            Assert.True(res.IsSuccessStatusCode, $"{slot}: {await res.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<TaskItem[]> Tasks(HttpClient client) => await Body<TaskItem[]>(await client.GetAsync(TasksUrl));

    private static async Task<ReviewHistoryItem[]> AuditOf(HttpClient reviewer, long id)
        => await Body<ReviewHistoryItem[]>(await reviewer.GetAsync($"{ReviewUrl(id)}/history"));

    // --- task inbox ----------------------------------------------------------------------------

    [Fact]
    public async Task ReturnedRequest_AppearsOnlyInItsOwnersTaskInbox_WithTheReviewersComment()
    {
        var (owner, returned) = await ReturnedRequest("صحح موقع العمل");
        var stillInReview = await Read(await Submit(owner.Client, await DraftReadyToSubmit(owner)));
        var draft = await CreateDraft(owner.Client);
        var (other, _) = await SubmittedRequest();

        var task = Assert.Single(await Tasks(owner.Client));
        Assert.Equal((returned.Id, returned.RequestNumber, RequestStatus.ReturnedForCompletion, ApplicantTaskKind.CompleteReturnedRequest),
            (task.RequestId, task.RequestNumber, task.Status, task.Kind));
        Assert.Equal("صحح موقع العمل", task.Instructions);
        Assert.Equal(returned.History[^1].OccurredAt, task.AssignedAt);
        Assert.Equal(("تصريح أعمال الحفر", "Excavation Works Permit"), (task.ServiceNameAr, task.ServiceNameEn));
        Assert.DoesNotContain(await Tasks(owner.Client), t => t.RequestId == stillInReview.Id || t.RequestId == draft.Id);

        Assert.Empty(await Tasks(other.Client));
        Assert.Empty(await Tasks(await Reviewer()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync(TasksUrl)).StatusCode);

        // The details show the same open task.
        Assert.Equal(new ApplicantTask(ApplicantTaskKind.CompleteReturnedRequest, "صحح موقع العمل", task.AssignedAt), returned.CurrentTask);
        Assert.True(returned.CanEdit);
        Assert.False(returned.CanUploadCoordinationLetters);
    }

    private static async Task<long> DraftReadyToSubmit(Applicant applicant)
    {
        var draft = await CreateDraft(applicant.Client);
        await Read(await SaveExcavation(applicant.Client, draft.Id, ValidExcavation(applicant.EstablishmentId)));
        await UploadAllRequired(applicant.Client, draft.Id);
        return draft.Id;
    }

    [Fact]
    public async Task TaskInbox_HidesRequests_WhenTheApplicantLeftTheEstablishment_AndActionsAreRefused()
    {
        var (applicant, returned) = await ReturnedRequest();
        var userId = (await applicant.Client.GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
            await db.EstablishmentMembers.Where(m => m.UserId == userId && m.EstablishmentId == applicant.EstablishmentId)
                .ExecuteDeleteAsync();
        }

        Assert.Empty(await Tasks(applicant.Client));
        foreach (var res in new[]
                 {
                     await SaveExcavation(applicant.Client, returned.Id, ValidExcavation(applicant.EstablishmentId)),
                     await Submit(applicant.Client, returned.Id),
                     await Upload(applicant.Client, returned.Id, "workRoutePlan", Pdf, "r.pdf", "application/pdf"),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal(ProblemCodes.NotYourEstablishment, await ProblemTitle(res));
        }
        Assert.Equal(RequestStatus.ReturnedForCompletion, (await Read(await applicant.Client.GetAsync(RequestUrl(returned.Id)))).Status);
    }

    // --- return → edit → resubmit --------------------------------------------------------------

    [Fact]
    public async Task ReturnedRequest_CanBeEditedAndSaved_WithoutResubmitting()
    {
        var (applicant, returned) = await ReturnedRequest();
        var changed = ValidExcavation(applicant.EstablishmentId) with { WorkDescription = "وصف محدث بعد ملاحظات مدن" };

        var saved = await Read(await SaveExcavation(applicant.Client, returned.Id, changed));
        var replaced = await Upload(applicant.Client, returned.Id, "ownerEntityLetter", Pdf, "signed-letter.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var now = await Read(await applicant.Client.GetAsync(RequestUrl(returned.Id)));
        Assert.Equal(RequestStatus.ReturnedForCompletion, saved.Status);
        Assert.Equal("وصف محدث بعد ملاحظات مدن", now.Excavation.WorkDescription);
        Assert.Equal("signed-letter.pdf", now.Attachments.Single(a => a.SlotCode == "ownerEntityLetter").FileName);
        Assert.Equal(returned.History.Length, now.History.Length); // saving is not a status change
        Assert.Equal(returned.RequestNumber, now.RequestNumber);
        Assert.Single(await Tasks(applicant.Client)); // still waiting for the resubmission
    }

    [Fact]
    public async Task ReturnedRequest_InvalidResubmissions_FailValidation_AndKeepTheStatus()
    {
        var (applicant, returned) = await ReturnedRequest();

        // A required field cleared and a required attachment removed while editing.
        await Read(await SaveExcavation(applicant.Client, returned.Id,
            ValidExcavation(applicant.EstablishmentId) with { WorkDescription = null, IssueDate = null }));
        Assert.Equal(HttpStatusCode.NoContent,
            (await applicant.Client.DeleteAsync($"{RequestUrl(returned.Id)}/attachments/trafficSafetyPlan")).StatusCode);

        var errors = await Errors(await Submit(applicant.Client, returned.Id));

        Assert.Equal([ErrorCodes.Required], errors["workDescription"]);
        Assert.Equal([ErrorCodes.Required], errors["issueDate"]);
        Assert.Equal([ErrorCodes.Required], errors["attachments.trafficSafetyPlan"]);
        var after = await Read(await applicant.Client.GetAsync(RequestUrl(returned.Id)));
        Assert.Equal(RequestStatus.ReturnedForCompletion, after.Status);
        Assert.Equal(returned.History.Length, after.History.Length);
    }

    [Fact]
    public async Task ReturnedRequest_CannotMoveToAnotherEstablishment()
    {
        var (applicant, returned) = await ReturnedRequest();
        var userId = (await applicant.Client.GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas"))!.Id;
        long otherEstablishment;
        using (var scope = factory.Services.CreateScope())
        {
            otherEstablishment = (await DevUserSeeder.EnsureEstablishmentMemberAsync(
                scope.ServiceProvider.GetRequiredService<ModonDbContext>(),
                Random.Shared.NextInt64(7_000_000_000, 7_999_999_999).ToString(), "منشأة ثانية", "Second establishment", userId)).Id;
        }

        var errors = await Errors(await SaveExcavation(applicant.Client, returned.Id,
            ValidExcavation(otherEstablishment)));

        Assert.Equal([ErrorCodes.CannotChange], errors["establishmentId"]);
        var after = await Read(await applicant.Client.GetAsync(RequestUrl(returned.Id)));
        Assert.Equal(applicant.EstablishmentId, after.Excavation.EstablishmentId);
    }

    [Fact]
    public async Task ValidResubmission_ReturnsToInProgress_KeepingTheSameRequestAndNumber()
    {
        var (applicant, returned) = await ReturnedRequest("حدّث الجدول الزمني");
        await Read(await SaveExcavation(applicant.Client, returned.Id,
            ValidExcavation(applicant.EstablishmentId) with { WorkLocation = "شارع 14" }));

        var resubmitted = await Read(await Submit(applicant.Client, returned.Id));

        Assert.Equal(RequestStatus.InProgress, resubmitted.Status);
        Assert.Equal((returned.Id, returned.RequestNumber, returned.SubmittedAt), (resubmitted.Id, resubmitted.RequestNumber, resubmitted.SubmittedAt));
        Assert.False(resubmitted.CanEdit);
        Assert.Null(resubmitted.CurrentTask);
        Assert.Equal("شارع 14", resubmitted.Excavation.WorkLocation);
        Assert.Collection(resubmitted.History,
            h => Assert.Equal(RequestAction.Create, h.Action),
            h => Assert.Equal(RequestAction.Submit, h.Action),
            h => Assert.Equal((RequestStatus.InProgress, RequestStatus.ReturnedForCompletion, RequestAction.ReturnForCompletion, "حدّث الجدول الزمني"),
                (h.FromStatus, h.ToStatus, h.Action, h.Comment)),
            h => Assert.Equal((RequestStatus.ReturnedForCompletion, RequestStatus.InProgress, RequestAction.Resubmit),
                (h.FromStatus, h.ToStatus, h.Action)));

        // One request, not a copy; it left the task inbox and is back in the reviewer's queue.
        var mine = await Body<RequestSummary[]>(await applicant.Client.GetAsync(RequestsUrl));
        Assert.Single(mine, r => r.RequestNumber == returned.RequestNumber);
        Assert.Empty(await Tasks(applicant.Client));
        var reviewer = await Reviewer();
        var review = await Body<ReviewRequestDetail>(await reviewer.GetAsync(ReviewUrl(returned.Id)));
        Assert.Equal(RequestStatus.InProgress, review.Status);
        Assert.Contains(RequestAction.Approve, review.AvailableDecisions);
        Assert.True(review.History[^1].ActorIsApplicant);

        // Locked again: no edits, and resubmitting twice adds nothing.
        Assert.Equal(HttpStatusCode.Conflict, (await SaveExcavation(applicant.Client, returned.Id, ValidExcavation(applicant.EstablishmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(applicant.Client, returned.Id)).StatusCode);
        Assert.Equal(4, (await AuditOf(reviewer, returned.Id)).Length);
    }

    [Fact]
    public async Task OrdinarySubmittedRequest_CannotBeEditedByTheApplicant()
    {
        var (applicant, submitted) = await SubmittedRequest();
        var (lettersApplicant, awaiting) = await AwaitingLetters();
        var id = submitted.Id;

        Assert.Equal(HttpStatusCode.Conflict, (await SaveExcavation(applicant.Client, id, ValidExcavation(applicant.EstablishmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(applicant.Client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(applicant.Client, id, "workRoutePlan", Pdf, "r.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await applicant.Client.DeleteAsync($"{RequestUrl(id)}/attachments/workRoutePlan")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(applicant.Client, id, LetterSlots[0], Pdf, "l.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitLetters(applicant.Client, id)).StatusCode);

        // Waiting for letters does not reopen the application form either.
        Assert.False(awaiting.CanEdit);
        Assert.Equal(HttpStatusCode.Conflict,
            (await SaveExcavation(lettersApplicant.Client, awaiting.Id, ValidExcavation(lettersApplicant.EstablishmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Upload(lettersApplicant.Client, awaiting.Id, "workRoutePlan", Pdf, "r.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(lettersApplicant.Client, awaiting.Id)).StatusCode);

        var after = await Read(await applicant.Client.GetAsync(RequestUrl(id)));
        Assert.Equal((RequestStatus.InProgress, 2), (after.Status, after.History.Length));
    }

    // --- coordination letters ------------------------------------------------------------------

    [Fact]
    public async Task Reviewer_CanRequestCoordinationLetters_ForAnInProgressRequestOnly()
    {
        var (applicant, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();
        var before = await Body<ReviewRequestDetail>(await reviewer.GetAsync(ReviewUrl(submitted.Id)));
        Assert.Equal([RequestAction.Approve, RequestAction.RequestCoordinationLetters, RequestAction.ReturnForCompletion, RequestAction.Reject],
            before.AvailableDecisions);
        Assert.Equal(LetterSlots, before.CoordinationLetterSlots.Select(s => s.Code));

        var tooLong = await Decide(reviewer, submitted.Id, "request-coordination-letters", new { comment = new string('x', 2001) });
        Assert.Equal([ErrorCodes.TooLong], (await Errors(tooLong))["comment"]);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Decide(applicant.Client, submitted.Id, "request-coordination-letters")).StatusCode);

        var awaiting = await Body<ReviewRequestDetail>(
            await Decide(reviewer, submitted.Id, "request-coordination-letters", new { comment = "  الخطابات الثلاثة مطلوبة  " }));

        Assert.Equal(RequestStatus.AwaitingCoordinationLetters, awaiting.Status);
        Assert.Empty(awaiting.AvailableDecisions);
        Assert.Equal(3, awaiting.History.Length);
        var last = awaiting.History[^1];
        Assert.Equal((RequestStatus.InProgress, RequestStatus.AwaitingCoordinationLetters, RequestAction.RequestCoordinationLetters),
            (last.FromStatus, last.ToStatus, last.Action));
        Assert.Equal(("الخطابات الثلاثة مطلوبة", "reviewer@modon.local", false), (last.Comment, last.ActorEmail, last.ActorIsApplicant));

        // Not again, and no other decision while waiting for the applicant.
        foreach (var action in new[] { "request-coordination-letters", "approve", "return" })
        {
            var res = await Decide(reviewer, submitted.Id, action, new { comment = "c" });
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal(ProblemCodes.InvalidTransition, await ProblemTitle(res));
        }
        Assert.Equal(3, (await AuditOf(reviewer, submitted.Id)).Length);

        // Drafts are invisible to reviewers; decided requests stay decided.
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(reviewer, (await CreateDraft(applicant.Client)).Id, "request-coordination-letters")).StatusCode);
        var (_, approved) = await SubmittedRequest();
        await Body<ReviewRequestDetail>(await Decide(reviewer, approved.Id, "approve"));
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(reviewer, approved.Id, "request-coordination-letters")).StatusCode);
    }

    [Fact]
    public async Task CoordinationLetterRequest_AppearsInTheCorrectTaskInbox()
    {
        var (applicant, awaiting) = await AwaitingLetters("يرجى رفع الخطابات قبل نهاية الأسبوع");
        var (other, _) = await SubmittedRequest();

        var task = Assert.Single(await Tasks(applicant.Client));

        Assert.Equal((awaiting.Id, RequestStatus.AwaitingCoordinationLetters, ApplicantTaskKind.UploadCoordinationLetters),
            (task.RequestId, task.Status, task.Kind));
        Assert.Equal("يرجى رفع الخطابات قبل نهاية الأسبوع", task.Instructions);
        Assert.Empty(await Tasks(other.Client));
        Assert.True(awaiting.CanUploadCoordinationLetters);
        Assert.Equal(LetterSlots, awaiting.CoordinationLetterSlots.Select(s => s.Code));
        Assert.All(awaiting.CoordinationLetterSlots, s =>
        {
            Assert.Equal((true, 20480), (s.RequiredOnSubmit, s.MaxSizeKb));
            Assert.Equal([".jpeg", ".pdf", ".jpg", ".png"], s.Extensions);
        });
    }

    [Fact]
    public async Task CoordinationLetterUploads_RejectMissingInvalidOversizedAndUnauthorizedFiles()
    {
        var (applicant, awaiting) = await AwaitingLetters();
        var (intruder, _) = await SubmittedRequest();
        var slot = LetterSlots[1];
        var field = $"attachments.{slot}";

        async Task AssertError(HttpResponseMessage res, string code) => Assert.Equal([code], (await Errors(res))[field]);

        var noFile = await applicant.Client.PostAsync($"{RequestUrl(awaiting.Id)}/attachments/{slot}",
            new MultipartFormDataContent { { new StringContent("no file attached"), "note" } });
        await AssertError(noFile, ErrorCodes.Required);
        await AssertError(await Upload(applicant.Client, awaiting.Id, slot, [], "l.pdf", "application/pdf"), ErrorCodes.FileEmpty);
        await AssertError(await Upload(applicant.Client, awaiting.Id, slot, Pdf, "l.docx", "application/octet-stream"), ErrorCodes.FileTypeNotAllowed);
        await AssertError(await Upload(applicant.Client, awaiting.Id, slot, Pdf, "l.gif", "image/gif"), ErrorCodes.FileTypeNotAllowed);
        await AssertError(await Upload(applicant.Client, awaiting.Id, slot, Pdf, "l.png", "image/png"), ErrorCodes.FileContentMismatch);
        var tooBig = new byte[ServiceApplications.MaxFileSizeKb * 1024 + 1];
        Pdf.CopyTo(tooBig, 0);
        await AssertError(await Upload(applicant.Client, awaiting.Id, slot, tooBig, "big.pdf", "application/pdf"), ErrorCodes.FileTooLarge);

        var unknown = await Upload(applicant.Client, awaiting.Id, "anotherLetter", Pdf, "l.pdf", "application/pdf");
        Assert.Equal((HttpStatusCode.NotFound, ProblemCodes.UnknownSlot), (unknown.StatusCode, await ProblemTitle(unknown)));

        // Other users cannot upload: another contractor, a reviewer (not the applicant API) or anonymous.
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(intruder.Client, awaiting.Id, slot, Pdf, "l.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(await Reviewer(), awaiting.Id, slot, Pdf, "l.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(factory.NewClient(), awaiting.Id, slot, Pdf, "l.pdf", "application/pdf")).StatusCode);

        // Nothing was stored, and sending the letters needs all three.
        Assert.DoesNotContain((await Read(await applicant.Client.GetAsync(RequestUrl(awaiting.Id)))).Attachments,
            a => LetterSlots.Contains(a.SlotCode));
        (await Upload(applicant.Client, awaiting.Id, LetterSlots[0], Jpeg, "sec.jpg", "image/jpeg")).EnsureSuccessStatusCode();
        var missing = await Errors(await SubmitLetters(applicant.Client, awaiting.Id));
        Assert.Equal(LetterSlots.Skip(1).Select(s => $"attachments.{s}").Order(), missing.Keys.Order());
        Assert.All(missing.Values, v => Assert.Equal([ErrorCodes.Required], v));
        var after = await Read(await applicant.Client.GetAsync(RequestUrl(awaiting.Id)));
        Assert.Equal((RequestStatus.AwaitingCoordinationLetters, awaiting.History.Length), (after.Status, after.History.Length));
    }

    [Fact]
    public async Task ValidSubmissionOfAllThreeLetters_ReturnsToInProgress()
    {
        var (applicant, awaiting) = await AwaitingLetters();
        await UploadAllLetters(applicant.Client, awaiting.Id);
        // Replace one letter, and remove and re-add another, before sending.
        (await Upload(applicant.Client, awaiting.Id, LetterSlots[1], Pdf, "grid-v2.pdf", "application/pdf")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await applicant.Client.DeleteAsync($"{RequestUrl(awaiting.Id)}/attachments/{LetterSlots[2]}")).StatusCode);
        Assert.Equal(400, (int)(await SubmitLetters(applicant.Client, awaiting.Id)).StatusCode);
        (await Upload(applicant.Client, awaiting.Id, LetterSlots[2], Pdf, "east.pdf", "application/pdf")).EnsureSuccessStatusCode();

        var sent = await Read(await SubmitLetters(applicant.Client, awaiting.Id));

        Assert.Equal(RequestStatus.InProgress, sent.Status);
        Assert.Equal((awaiting.Id, awaiting.RequestNumber), (sent.Id, sent.RequestNumber));
        Assert.False(sent.CanUploadCoordinationLetters);
        Assert.Null(sent.CurrentTask);
        var last = sent.History[^1];
        Assert.Equal((RequestStatus.AwaitingCoordinationLetters, RequestStatus.InProgress, RequestAction.SubmitCoordinationLetters),
            (last.FromStatus, last.ToStatus, last.Action));
        Assert.Equal(9, sent.Attachments.Length); // six application documents + three letters
        Assert.Equal("grid-v2.pdf", sent.Attachments.Single(a => a.SlotCode == LetterSlots[1]).FileName);
        Assert.Empty(await Tasks(applicant.Client));

        // The letters are locked now, sending again changes nothing, and the reviewer can read them.
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(applicant.Client, awaiting.Id, LetterSlots[0], Pdf, "l.pdf", "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await applicant.Client.DeleteAsync($"{RequestUrl(awaiting.Id)}/attachments/{LetterSlots[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitLetters(applicant.Client, awaiting.Id)).StatusCode);
        var reviewer = await Reviewer();
        var review = await Body<ReviewRequestDetail>(await reviewer.GetAsync(ReviewUrl(awaiting.Id)));
        Assert.Equal(4, review.History.Length);
        Assert.True(review.History[^1].ActorIsApplicant);
        Assert.Contains(RequestAction.Approve, review.AvailableDecisions);
        var download = await reviewer.GetAsync($"{ReviewUrl(awaiting.Id)}/attachments/{LetterSlots[0]}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync());
    }

    // --- history, finality, concurrency, privacy ----------------------------------------------

    [Fact]
    public async Task History_RecordsEveryTransitionOfTheFullCycle_AndDecisionsStayFinal()
    {
        var (applicant, submitted) = await SubmittedRequest();
        var reviewer = await Reviewer();
        var id = submitted.Id;

        await Body<ReviewRequestDetail>(await Decide(reviewer, id, "return", new { comment = "أكمل النواقص" }));
        await Read(await Submit(applicant.Client, id));
        await Body<ReviewRequestDetail>(await Decide(reviewer, id, "request-coordination-letters", new { comment = "الخطابات" }));
        await UploadAllLetters(applicant.Client, id);
        await Read(await SubmitLetters(applicant.Client, id));
        await Body<ReviewRequestDetail>(await Decide(reviewer, id, "approve", new { comment = "معتمد" }));

        var audit = await AuditOf(reviewer, id);
        var reviewerId = audit.Single(h => h.Action == RequestAction.Approve).ActorUserId;
        Assert.Equal(
        [
            (null, RequestStatus.Draft, RequestAction.Create, true, null),
            (RequestStatus.Draft, RequestStatus.InProgress, RequestAction.Submit, true, null),
            (RequestStatus.InProgress, RequestStatus.ReturnedForCompletion, RequestAction.ReturnForCompletion, false, "أكمل النواقص"),
            (RequestStatus.ReturnedForCompletion, RequestStatus.InProgress, RequestAction.Resubmit, true, null),
            (RequestStatus.InProgress, RequestStatus.AwaitingCoordinationLetters, RequestAction.RequestCoordinationLetters, false, "الخطابات"),
            (RequestStatus.AwaitingCoordinationLetters, RequestStatus.InProgress, RequestAction.SubmitCoordinationLetters, true, null),
            (RequestStatus.InProgress, RequestStatus.Approved, RequestAction.Approve, false, "معتمد"),
        ], audit.Select(h => ((RequestStatus?)h.FromStatus, h.ToStatus, h.Action, h.ActorIsApplicant, h.Comment)).ToArray());
        Assert.All(audit.Where(h => !h.ActorIsApplicant), h => Assert.Equal(reviewerId, h.ActorUserId));
        Assert.Equal(audit.OrderBy(h => h.OccurredAt).Select(h => h.Action), audit.Select(h => h.Action));

        // The approval cannot be reversed from either side.
        foreach (var res in new[]
                 {
                     await Decide(reviewer, id, "return", new { comment = "c" }),
                     await Decide(reviewer, id, "request-coordination-letters"),
                     await Submit(applicant.Client, id),
                     await SubmitLetters(applicant.Client, id),
                     await SaveExcavation(applicant.Client, id, ValidExcavation(applicant.EstablishmentId)),
                 })
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(7, (await AuditOf(reviewer, id)).Length);
        Assert.Equal(RequestStatus.Approved, (await Read(await applicant.Client.GetAsync(RequestUrl(id)))).Status);
    }

    [Fact]
    public async Task RepeatedOrSimultaneousApplicantActions_RecordOneTransitionOnly()
    {
        var (returnedApplicant, returned) = await ReturnedRequest();
        var (lettersApplicant, awaiting) = await AwaitingLetters();
        await UploadAllLetters(lettersApplicant.Client, awaiting.Id);

        var resubmits = await Task.WhenAll(Submit(returnedApplicant.Client, returned.Id), Submit(returnedApplicant.Client, returned.Id));
        var letters = await Task.WhenAll(SubmitLetters(lettersApplicant.Client, awaiting.Id), SubmitLetters(lettersApplicant.Client, awaiting.Id));

        foreach (var results in new[] { resubmits, letters })
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        var reviewer = await Reviewer();
        Assert.Single(await AuditOf(reviewer, returned.Id), h => h.Action == RequestAction.Resubmit);
        Assert.Single(await AuditOf(reviewer, awaiting.Id), h => h.Action == RequestAction.SubmitCoordinationLetters);
    }

    [Fact]
    public async Task OtherUsers_CannotSeeOrChangeTheseRequestsOrTheirAttachments()
    {
        var (owner, awaiting) = await AwaitingLetters();
        await UploadAllLetters(owner.Client, awaiting.Id);
        var (returnedOwner, returned) = await ReturnedRequest();
        var intruders = new[] { (await NewContractor()).Client, await factory.LoginAs("investor@modon.local"), await factory.LoginAs("contractor@modon.local") };

        foreach (var intruder in intruders)
        {
            foreach (var res in new[]
                     {
                         await intruder.GetAsync(RequestUrl(awaiting.Id)),
                         await intruder.GetAsync($"{RequestUrl(awaiting.Id)}/attachments/{LetterSlots[0]}"),
                         await intruder.GetAsync($"{RequestUrl(returned.Id)}/attachments/workRoutePlan"),
                         await intruder.DeleteAsync($"{RequestUrl(awaiting.Id)}/attachments/{LetterSlots[0]}"),
                         await SubmitLetters(intruder, awaiting.Id),
                         await SaveExcavation(intruder, returned.Id, ValidExcavation(returnedOwner.EstablishmentId)),
                         await Submit(intruder, returned.Id),
                     })
            {
                Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
                var body = await res.Content.ReadAsStringAsync();
                Assert.DoesNotContain(awaiting.RequestNumber, body);
                Assert.DoesNotContain(returned.RequestNumber, body);
            }
            Assert.DoesNotContain(await Tasks(intruder), t => t.RequestId == awaiting.Id || t.RequestId == returned.Id);
            Assert.Equal(HttpStatusCode.Forbidden, (await intruder.GetAsync(ReviewUrl(awaiting.Id))).StatusCode);
        }

        // Nothing changed for the owners.
        Assert.Equal(3, (await Read(await owner.Client.GetAsync(RequestUrl(awaiting.Id)))).Attachments.Count(a => LetterSlots.Contains(a.SlotCode)));
        Assert.Equal(RequestStatus.ReturnedForCompletion, (await Read(await returnedOwner.Client.GetAsync(RequestUrl(returned.Id)))).Status);
    }
}
