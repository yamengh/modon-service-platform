using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Review;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests.Camunda;

/// <summary>
/// The excavation lifecycle with the Camunda workflow engine enabled (production adapter + SDK, fake cluster).
/// Every test checks both sides: the request in the database and the process instance in "Camunda".
/// </summary>
[Collection("Camunda API")]
public class CamundaWorkflowTests
{
    private readonly CamundaAppFactory _factory;
    private FakeCamunda Camunda => _factory.Camunda;

    private static readonly string[] LetterSlots =
        ServiceApplications.ExcavationPermit.CoordinationSlots.Select(s => s.Code).ToArray();

    public CamundaWorkflowTests(CamundaAppFactory factory)
    {
        _factory = factory;
        factory.Camunda.Reset();
    }

    // --- helpers -------------------------------------------------------------------------------

    private sealed record Applicant(HttpClient Client, long EstablishmentId);

    private async Task<(Applicant Applicant, RequestDetail Draft)> ReadyDraft()
    {
        var (email, establishmentId) = await _factory.CreateUserAsync([ModonRoles.Contractor]);
        var client = await _factory.LoginAs(email);
        var draft = await CreateDraft(client);
        await Read(await SaveExcavation(client, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(client, draft.Id);
        return (new Applicant(client, establishmentId), draft);
    }

    private async Task<(Applicant Applicant, RequestDetail Request)> Submitted()
    {
        var (applicant, draft) = await ReadyDraft();
        return (applicant, await Read(await Submit(applicant.Client, draft.Id)));
    }

    private Task<HttpClient> Reviewer() => _factory.LoginAs("reviewer@modon.local");

    private static Task<HttpResponseMessage> Decide(HttpClient reviewer, long id, string action, object? body = null)
        => reviewer.PostAsJsonAsync($"/api/modon/review/requests/{id}/{action}", body ?? new { comment = "c" }, Json);

    private static Task<HttpResponseMessage> SubmitLetters(HttpClient client, long id)
        => client.PostAsync($"{RequestsUrl}/{id}/coordination-letters/submit", null);

    private static async Task UploadLetters(HttpClient client, long id)
    {
        foreach (var slot in LetterSlots)
            Assert.True((await Upload(client, id, slot, Pdf, $"{slot}.pdf", "application/pdf")).IsSuccessStatusCode);
    }

    private static async Task<T> Body<T>(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<ServiceRequest> Row(long id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModonDbContext>().ServiceRequests.AsNoTracking()
            .Include(r => r.History).SingleAsync(r => r.Id == id);
    }

    /// <summary>The single process instance of a request, after checking there is exactly one.</summary>
    private FakeCamunda.Instance InstanceOf(RequestDetail request)
        => Assert.Single(Camunda.InstancesFor(request.RequestNumber));

    private static async Task AssertProblem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(code, await ProblemTitle(res));
    }

    // --- process start and correlation --------------------------------------------------------

    [Fact]
    public async Task FirstSubmission_StartsOneProcessInstance_AndStoresItsCorrelation()
    {
        var (applicant, draft) = await ReadyDraft();
        Assert.Empty(Camunda.InstancesFor(draft.RequestNumber)); // drafts have no process

        var submitted = await Read(await Submit(applicant.Client, draft.Id));

        var instance = InstanceOf(submitted);
        Assert.Equal("Gateway_ModonReview", instance.CurrentElement); // waiting for Modon's review
        Assert.Equal(draft.Id, (long)instance.Variables["requestId"]!);
        Assert.Equal(draft.RequestNumber, (string?)instance.Variables["requestNumber"]);
        Assert.StartsWith($"{draft.RequestNumber}:", instance.CorrelationKey);

        var row = await Row(draft.Id);
        Assert.Equal((instance.Key, instance.CorrelationKey, FakeCamunda.DefinitionKey, 2),
            (row.WorkflowInstanceKey, row.WorkflowCorrelationKey, row.WorkflowDefinitionKey, row.WorkflowDefinitionVersion));
        Assert.Equal(RequestStatus.InProgress, row.Status);

        var review = await Body<ReviewRequestDetail>(await (await Reviewer()).GetAsync($"/api/modon/review/requests/{draft.Id}"));
        Assert.Equal(new ReviewWorkflowInfo("camunda", instance.Key, 2), review.Workflow);
        Assert.Single(Camunda.CallsTo("/v2/process-instances"), c => c.Method == "POST");
    }

    [Fact]
    public async Task ReturnAndResubmit_ContinueTheSameProcessInstance_TwoRounds()
    {
        var (applicant, submitted) = await Submitted();
        var reviewer = await Reviewer();
        var instanceKey = InstanceOf(submitted).Key;

        for (var round = 1; round <= 2; round++)
        {
            await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "return", new { comment = $"round {round}" }));
            Assert.Equal("Event_AwaitResubmission", InstanceOf(submitted).CurrentElement);
            Assert.Equal(RequestStatus.ReturnedForCompletion, (await Row(submitted.Id)).Status);

            await Read(await SaveExcavation(applicant.Client, submitted.Id,
                ValidExcavation(applicant.EstablishmentId) with { WorkLocation = $"شارع {round}" })); // editing is not a process step
            Assert.Equal("Event_AwaitResubmission", InstanceOf(submitted).CurrentElement);

            var resubmitted = await Read(await Submit(applicant.Client, submitted.Id));
            Assert.Equal(RequestStatus.InProgress, resubmitted.Status);
            Assert.Equal("Gateway_ModonReview", InstanceOf(submitted).CurrentElement);
        }

        // Still the one instance from the first submission; no second "create" was sent.
        Assert.Equal(instanceKey, InstanceOf(submitted).Key);
        Assert.Equal(instanceKey, (await Row(submitted.Id)).WorkflowInstanceKey);
        Assert.Single(Camunda.CallsTo("/v2/process-instances"), c => c.Method == "POST" && c.Body!.Contains(submitted.RequestNumber));
        var messages = Camunda.CallsTo("/v2/messages/correlation")
            .Select(c => JsonDocument.Parse(c.Body!).RootElement)
            .Where(b => b.GetProperty("correlationKey").GetString() == InstanceOf(submitted).CorrelationKey)
            .Select(b => b.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["ExcavationReturnedForCompletion", "ExcavationResubmitted", "ExcavationReturnedForCompletion", "ExcavationResubmitted"], messages);
    }

    [Fact]
    public async Task CoordinationLetterCycles_ThenApproval_CompleteTheSameInstance()
    {
        var (applicant, submitted) = await Submitted();
        var reviewer = await Reviewer();

        for (var round = 1; round <= 2; round++)
        {
            await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "request-coordination-letters", new { comment = (string?)null }));
            Assert.Equal("Event_AwaitCoordinationLetters", InstanceOf(submitted).CurrentElement);
            if (round == 1) await UploadLetters(applicant.Client, submitted.Id);
            var sent = await Read(await SubmitLetters(applicant.Client, submitted.Id));
            Assert.Equal(RequestStatus.InProgress, sent.Status);
            Assert.Equal("Gateway_ModonReview", InstanceOf(submitted).CurrentElement);
        }

        var approved = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "approve"));

        var instance = InstanceOf(submitted);
        Assert.Equal(("COMPLETED", "End_Approved"), (instance.State, instance.CurrentElement));
        Assert.Equal(RequestStatus.Approved, approved.Status);
        Assert.Equal(
            [RequestAction.Create, RequestAction.Submit, RequestAction.RequestCoordinationLetters, RequestAction.SubmitCoordinationLetters,
             RequestAction.RequestCoordinationLetters, RequestAction.SubmitCoordinationLetters, RequestAction.Approve],
            approved.History.Select(h => h.Action));
    }

    [Fact]
    public async Task Rejection_EndsTheProcess_AndNothingCanFollowIt()
    {
        var (applicant, submitted) = await Submitted();
        var reviewer = await Reviewer();

        var rejected = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "reject", new { reason = "غير مكتمل" }));

        Assert.Equal(RequestStatus.Rejected, rejected.Status);
        Assert.Equal(("COMPLETED", "End_Rejected"), (InstanceOf(submitted).State, InstanceOf(submitted).CurrentElement));
        foreach (var res in new[]
                 {
                     await Decide(reviewer, submitted.Id, "approve"),
                     await Decide(reviewer, submitted.Id, "return"),
                     await Submit(applicant.Client, submitted.Id),
                 })
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(3, (await Row(submitted.Id)).History.Count);
    }

    // --- duplicates and conflicts -------------------------------------------------------------

    [Fact]
    public async Task ADecisionCamundaNoLongerWaitsFor_IsRefused_WithoutChangingTheRequest()
    {
        var (_, submitted) = await Submitted();
        // Another channel (e.g. Operate) already completed the review in Camunda.
        Assert.True(Camunda.Deliver("ExcavationApproved", InstanceOf(submitted).CorrelationKey!));

        var res = await Decide(await Reviewer(), submitted.Id, "reject", new { reason = "late" });

        await AssertProblem(res, HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
        var row = await Row(submitted.Id);
        Assert.Equal((RequestStatus.InProgress, 2), (row.Status, row.History.Count));
        Assert.Equal("End_Approved", InstanceOf(submitted).CurrentElement); // only one terminal outcome
    }

    [Fact]
    public async Task SimultaneousConflictingDecisions_GiveOneOutcome_AndOneHistoryRow()
    {
        var (_, submitted) = await Submitted();
        var (first, second) = (await Reviewer(), await Reviewer());

        var results = await Task.WhenAll(
            Decide(first, submitted.Id, "approve"),
            Decide(second, submitted.Id, "reject", new { reason = "r" }));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var row = await Row(submitted.Id);
        Assert.Single(row.History, h => h.Action is RequestAction.Approve or RequestAction.Reject);
        var instance = InstanceOf(submitted);
        Assert.Equal("COMPLETED", instance.State);
        Assert.Equal(row.Status == RequestStatus.Approved ? "End_Approved" : "End_Rejected", instance.CurrentElement);
    }

    [Fact]
    public async Task RepeatedResubmissionsAndLetterSubmissions_DeliverOneMessage_AndOneHistoryRow()
    {
        var (applicant, submitted) = await Submitted();
        var reviewer = await Reviewer();
        await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "return"));

        var resubmits = await Task.WhenAll(Submit(applicant.Client, submitted.Id), Submit(applicant.Client, submitted.Id));

        Assert.Single(resubmits, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(resubmits, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single((await Row(submitted.Id)).History, h => h.Action == RequestAction.Resubmit);

        await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "request-coordination-letters"));
        await UploadLetters(applicant.Client, submitted.Id);
        var letters = await Task.WhenAll(SubmitLetters(applicant.Client, submitted.Id), SubmitLetters(applicant.Client, submitted.Id));

        Assert.Single(letters, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single((await Row(submitted.Id)).History, h => h.Action == RequestAction.SubmitCoordinationLetters);
        Assert.Equal("Gateway_ModonReview", InstanceOf(submitted).CurrentElement);
        Assert.Equal(RequestStatus.InProgress, (await Row(submitted.Id)).Status);
    }

    [Fact]
    public async Task AFailedDatabaseSaveAfterStart_CancelsTheNewInstance()
    {
        var (applicant, draft) = await ReadyDraft();
        // While Camunda starts the process, someone else changes the request (simulated directly in the database).
        Camunda.AfterCreate = _ =>
        {
            using var scope = _factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ModonDbContext>().ServiceRequests.Where(r => r.Id == draft.Id)
                .ExecuteUpdate(u => u.SetProperty(r => r.Status, RequestStatus.Cancelled));
        };

        var res = await Submit(applicant.Client, draft.Id);

        await AssertProblem(res, HttpStatusCode.Conflict, ProblemCodes.RequestNotEditable);
        var orphan = Assert.Single(Camunda.InstancesFor(draft.RequestNumber));
        Assert.Equal("TERMINATED", orphan.State); // compensated
        var row = await Row(draft.Id);
        Assert.Null(row.WorkflowInstanceKey);
        Assert.Single(row.History); // only "create"
    }

    // --- Camunda failures ---------------------------------------------------------------------

    [Fact]
    public async Task CamundaUnreachable_SubmissionAndDecisionsReturn503_AndNothingChanges()
    {
        var (applicant, draft) = await ReadyDraft();
        var (_, submitted) = await Submitted();
        Camunda.Down = true;

        await AssertProblem(await Submit(applicant.Client, draft.Id), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);
        await AssertProblem(await Decide(await Reviewer(), submitted.Id, "approve"), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);

        Assert.Equal((RequestStatus.Draft, 1, (string?)null), ((await Row(draft.Id)).Status, (await Row(draft.Id)).History.Count, (await Row(draft.Id)).WorkflowInstanceKey));
        Assert.Equal((RequestStatus.InProgress, 2), ((await Row(submitted.Id)).Status, (await Row(submitted.Id)).History.Count));

        // Back up: the same actions now succeed, with one instance per request.
        Camunda.Down = false;
        Assert.Equal(RequestStatus.InProgress, (await Read(await Submit(applicant.Client, draft.Id))).Status);
        Assert.Single(Camunda.InstancesFor(draft.RequestNumber));
        Assert.Equal(RequestStatus.Approved, (await Body<ReviewRequestDetail>(await Decide(await Reviewer(), submitted.Id, "approve"))).Status);
    }

    [Fact]
    public async Task ProcessNotDeployed_RefusesSubmission_WithServiceUnavailable()
    {
        var (applicant, draft) = await ReadyDraft();
        Camunda.Deployed = false;

        await AssertProblem(await Submit(applicant.Client, draft.Id), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);

        Assert.Equal(RequestStatus.Draft, (await Row(draft.Id)).Status);
        Assert.Empty(Camunda.InstancesFor(draft.RequestNumber));
    }

    [Fact]
    public async Task CamundaErrorResponses_AreNotRetried_AndReturn503()
    {
        var (_, submitted) = await Submitted();
        Camunda.FailNext["/v2/messages/correlation"] = HttpStatusCode.InternalServerError;

        await AssertProblem(await Decide(await Reviewer(), submitted.Id, "approve"), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);

        Assert.Single(Camunda.CallsTo("/v2/messages/correlation")); // the SDK's own retry is off
        Assert.Equal("Gateway_ModonReview", InstanceOf(submitted).CurrentElement);
    }

    [Fact]
    public async Task ADecisionCamundaAccepted_ButWhoseResponseWasLost_IsSavedOnRetry_Once()
    {
        var (_, submitted) = await Submitted();
        var reviewer = await Reviewer();
        Camunda.LoseResponseOfNext.Add("/v2/messages/correlation");

        await AssertProblem(await Decide(reviewer, submitted.Id, "approve"), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);
        Assert.Equal("End_Approved", InstanceOf(submitted).CurrentElement); // Camunda did it
        Assert.Equal(RequestStatus.InProgress, (await Row(submitted.Id)).Status); // the database did not

        var retried = await Body<ReviewRequestDetail>(await Decide(reviewer, submitted.Id, "approve"));

        Assert.Equal(RequestStatus.Approved, retried.Status);
        Assert.Single((await Row(submitted.Id)).History, h => h.Action == RequestAction.Approve);
        // A different decision cannot claim that outcome.
        var (_, other) = await Submitted();
        Camunda.LoseResponseOfNext.Add("/v2/messages/correlation");
        await Decide(reviewer, other.Id, "approve");
        await AssertProblem(await Decide(reviewer, other.Id, "reject", new { reason = "r" }), HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task AStartWhoseResponseWasLost_LeavesAnInstanceThatCanNeverReceiveTheRequestsMessages()
    {
        var (applicant, draft) = await ReadyDraft();
        Camunda.LoseResponseOfNext.Add("/v2/process-instances");

        await AssertProblem(await Submit(applicant.Client, draft.Id), HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);
        var lost = Assert.Single(Camunda.InstancesFor(draft.RequestNumber));

        await Read(await Submit(applicant.Client, draft.Id));
        await Body<ReviewRequestDetail>(await Decide(await Reviewer(), draft.Id, "approve"));

        var instances = Camunda.InstancesFor(draft.RequestNumber);
        Assert.Equal(2, instances.Length);
        var linkedKey = (await Row(draft.Id)).WorkflowInstanceKey;
        var linked = instances.Single(i => i.Key == linkedKey);
        Assert.NotEqual(lost.CorrelationKey, linked.CorrelationKey);
        Assert.Equal("End_Approved", linked.CurrentElement);
        Assert.Equal(("ACTIVE", "Gateway_ModonReview"), (lost.State, lost.CurrentElement)); // untouched orphan
    }

    [Fact]
    public async Task ARequestWithoutProcessInstance_IsRefusedWithWorkflowNotStarted()
    {
        var (_, submitted) = await Submitted();
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModonDbContext>().ServiceRequests.Where(r => r.Id == submitted.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.WorkflowInstanceKey, (string?)null).SetProperty(r => r.WorkflowCorrelationKey, (string?)null));

        await AssertProblem(await Decide(await Reviewer(), submitted.Id, "approve"), HttpStatusCode.Conflict, ProblemCodes.WorkflowNotStarted);
        Assert.Equal(RequestStatus.InProgress, (await Row(submitted.Id)).Status);
    }

    // --- diagnostics --------------------------------------------------------------------------

    [Fact]
    public async Task Diagnostics_ReportCamundaHonestly()
    {
        var reviewer = await Reviewer();

        var ok = await Body<WorkflowStatusReport>(await reviewer.GetAsync("/api/modon/review/workflow"));
        Assert.Equal(("camunda", true), (ok.Engine, ok.Ready));
        Assert.Equal((FakeCamunda.GatewayVersion, true, 2), (ok.Camunda!.GatewayVersion, ok.Camunda.ProcessDeployed, ok.Camunda.DeployedVersion));
        Assert.Equal(ok.Camunda.CheckedInVersionTag, ok.Camunda.DeployedVersionTag);
        Assert.Equal(HttpStatusCode.OK, (await _factory.NewClient().GetAsync("/health")).StatusCode);

        Camunda.Deployed = false;
        var undeployed = await Body<WorkflowStatusReport>(await reviewer.GetAsync("/api/modon/review/workflow"));
        Assert.False(undeployed.Ready);
        Assert.Contains("not deployed", undeployed.Summary);

        Camunda.Down = true;
        var down = await Body<WorkflowStatusReport>(await reviewer.GetAsync("/api/modon/review/workflow"));
        Assert.False(down.Ready);
        Assert.Contains("NOT reachable", down.Summary);
        Assert.Contains("camunda.test", down.Summary);
        var health = await _factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Equal("Unhealthy", await health.Content.ReadAsStringAsync()); // no details for anonymous callers

        var (applicant, _) = await ReadyDraft();
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.Client.GetAsync("/api/modon/review/workflow")).StatusCode);
    }

    [Fact]
    public async Task RequestWorkflowCheck_ComparesTheDatabaseWithCamunda()
    {
        var (_, submitted) = await Submitted();
        var reviewer = await Reviewer();
        var url = $"/api/modon/review/requests/{submitted.Id}/workflow";

        var inSync = await Body<RequestWorkflowReport>(await reviewer.GetAsync(url));
        Assert.Equal((true, "Gateway_ModonReview", RequestStatus.InProgress, "ACTIVE"),
            (inSync.InSync, inSync.EngineElementId, inSync.EngineStatus, inSync.EngineState));
        Assert.Equal(InstanceOf(submitted).Key, inSync.ProcessInstanceKey);

        Camunda.Deliver("ExcavationCoordinationLettersRequested", InstanceOf(submitted).CorrelationKey!);
        var drift = await Body<RequestWorkflowReport>(await reviewer.GetAsync(url));
        Assert.Equal((false, RequestStatus.AwaitingCoordinationLetters, RequestStatus.InProgress),
            (drift.InSync, drift.EngineStatus, drift.DatabaseStatus));

        Camunda.Down = true;
        var unknown = await Body<RequestWorkflowReport>(await reviewer.GetAsync(url));
        Assert.Null(unknown.InSync);
        Assert.Contains("Cannot reach Camunda", unknown.Note);
    }
}
