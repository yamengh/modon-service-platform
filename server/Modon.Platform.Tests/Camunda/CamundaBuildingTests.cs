using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Requests.Workflow.Camunda;
using Modon.Platform.Review;
using static Modon.Platform.Tests.BuildingTestKit;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests.Camunda;

/// <summary>
/// The building licence with the Camunda engine (production adapter + SDK, fake cluster running the checked-in
/// building-licence.bpmn). Every test checks the request in the database and the process instance in "Camunda".
/// </summary>
[Collection("Camunda API")]
public class CamundaBuildingTests
{
    private readonly CamundaAppFactory _factory;
    private readonly BuildingTestKit _kit;
    private FakeCamunda Camunda => _factory.Camunda;

    public CamundaBuildingTests(CamundaAppFactory factory)
    {
        _factory = factory;
        _kit = new BuildingTestKit(factory);
        factory.Camunda.Reset();
    }

    private FakeCamunda.Instance InstanceOf(RequestDetail r) => Camunda.InstancesFor(r.RequestNumber).Single();

    private async Task<ServiceRequest> Row(long id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModonDbContext>().ServiceRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<int> Count<T>(Func<ModonDbContext, IQueryable<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ModonDbContext>()).CountAsync();
    }

    private async Task<RequestWorkflowReport> Check(long id)
        => (await (await _kit.Reviewer()).GetFromJsonAsync<RequestWorkflowReport>($"/api/modon/review/requests/{id}/workflow", Json))!;

    private void AssertWaitsAt(RequestDetail request, string elementId)
    {
        var instance = InstanceOf(request);
        Assert.Equal(("ACTIVE", elementId), (instance.State, instance.CurrentElement));
    }

    [Fact]
    public async Task Submit_StartsTheSeparateBuildingProcess_Once()
    {
        var (_, submitted) = await _kit.Submitted();

        var instance = InstanceOf(submitted);
        Assert.Equal(BuildingLicenceProcess.DefaultProcessId, instance.ProcessId);
        Assert.Equal("Gateway_ConsultantSelection", instance.CurrentElement);
        Assert.Equal(submitted.RequestNumber, instance.BusinessId);
        var row = await Row(submitted.Id);
        Assert.Equal(instance.Key, row.WorkflowInstanceKey);
        Assert.Equal(instance.CorrelationKey, row.WorkflowCorrelationKey);
        Assert.StartsWith(submitted.RequestNumber + ":", row.WorkflowCorrelationKey);
        Assert.Equal(1, row.WorkflowDefinitionVersion); // the fake deploys the building process as version 1

        // The excavation permit still starts its own process.
        var (email, establishmentId) = await _factory.CreateUserAsync([ModonRoles.Contractor]);
        var contractor = await _factory.LoginAs(email);
        var excavation = await CreateDraft(contractor);
        await Read(await SaveExcavation(contractor, excavation.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(contractor, excavation.Id);
        await Read(await Submit(contractor, excavation.Id));
        Assert.Equal(ExcavationProcess.DefaultProcessId, Camunda.InstancesFor(excavation.RequestNumber).Single().ProcessId);
    }

    [Fact]
    public async Task WholeLifecycle_FollowsTheBpmn_OnOneInstance()
    {
        var (investor, request) = await _kit.Submitted();
        var reviewer = await _kit.Reviewer();

        // Consultant: open project; quotations are data in PostgreSQL only, not messages.
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        AssertWaitsAt(request, "Gateway_ConsultantTender");
        var consultant = await _kit.Consultant();
        var messagesBefore = Camunda.CallsTo("messages/correlation").Length;
        var quote = (await Project(await Quote(consultant.Client, request.Id, consultant.EstablishmentId, 150_000m))).MyQuotations.Single();
        Assert.Equal(messagesBefore, Camunda.CallsTo("messages/correlation").Length);
        await Read(await AcceptQuotation(investor.Client, request.Id, "consultant", quote.Id));
        AssertWaitsAt(request, "Gateway_ContractorSelection");

        // Contractor: a specific contractor rejects (back to selection), another accepts (to Modon).
        var declining = await _kit.Contractor();
        await Read(await Choose(investor.Client, request.Id, "contractor", SelectionMode.Direct, declining.EstablishmentId));
        AssertWaitsAt(request, "Gateway_ContractorDecision");
        Assert.Equal(HttpStatusCode.NoContent, (await Respond(declining.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "busy"))).StatusCode);
        AssertWaitsAt(request, "Gateway_ContractorSelection");
        await _kit.ContractorAcceptsDirectly(investor, request.Id);
        AssertWaitsAt(request, "Gateway_LicenceReview");
        Assert.Equal((true, RequestStatus.UnderModonReview), ((await Check(request.Id)).InSync, (await Check(request.Id)).EngineStatus));

        // Modon approves; every monthly report is one message and the instance waits for the next one.
        Assert.Equal(HttpStatusCode.OK, (await Decide(reviewer, request.Id, "approve")).StatusCode);
        AssertWaitsAt(request, "Event_AwaitProgressReport");
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        await Project(await Report(consultant.Client, request.Id, month, Progress(20, 10, 0, 0, 0, 0)));
        AssertWaitsAt(request, "Event_AwaitProgressReport");
        var reportMessages = Camunda.CallsTo("messages/correlation").Count(c => c.Body!.Contains("BuildingProgressReportSubmitted"));
        Assert.Equal(1, reportMessages);
        Assert.Equal((true, RequestStatus.Approved), ((await Check(request.Id)).InSync, (await Check(request.Id)).EngineStatus));

        // One instance from start to end, every message on this request's correlation key.
        var instance = InstanceOf(request);
        Assert.All(Camunda.CallsTo("messages/correlation").Where(c => c.Body!.Contains(instance.CorrelationKey!)),
            c => Assert.Contains("\"Building", c.Body));
        Assert.Equal("submitProgressReport", (string?)instance.Variables["lastAction"]);
    }

    [Fact]
    public async Task RejectionsLoopBack_AndARejectedLicenceEndsTheProcess()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();

        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));
        await Read(await RejectQuotations(investor.Client, request.Id, "consultant"));
        AssertWaitsAt(request, "Gateway_ConsultantSelection");
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));
        await Project(await Respond(consultant.Client, request.Id, ConsultantAccepts()));
        AssertWaitsAt(request, "Gateway_ContractorSelection");
        await Read(await Choose(investor.Client, request.Id, "contractor", SelectionMode.OpenTender));
        var contractor = await _kit.Contractor();
        var quote = (await Project(await Quote(contractor.Client, request.Id, contractor.EstablishmentId, 9_000_000m))).MyQuotations.Single();
        await Read(await AcceptQuotation(investor.Client, request.Id, "contractor", quote.Id));
        AssertWaitsAt(request, "Gateway_LicenceReview");

        var rejected = await Decide(await _kit.Reviewer(), request.Id, "reject", new { reason = "غير مطابق" });

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var instance = InstanceOf(request);
        Assert.Equal(("COMPLETED", "End_LicenceRejected"), (instance.State, instance.CurrentElement));
        var check = await Check(request.Id);
        Assert.Equal((true, RequestStatus.Rejected, "COMPLETED"), (check.InSync, check.EngineStatus, check.EngineState));
        Assert.Single(Camunda.InstancesFor(request.RequestNumber));
    }

    [Fact]
    public async Task BuildingProcessNotDeployed_RefusesSubmission_AndLeavesTheDraft()
    {
        Camunda.NotDeployed.Add(BuildingLicenceProcess.DefaultProcessId);
        var (investor, draft) = await _kit.ReadyDraft();

        var res = await Submit(investor.Client, draft.Id);

        await AssertProblem(res, HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);
        var after = await Get(investor.Client, draft.Id);
        Assert.Equal((RequestStatus.Draft, 1, null), (after.Status, after.History.Length, (await Row(draft.Id)).WorkflowInstanceKey));
        var status = (await (await _kit.Reviewer()).GetFromJsonAsync<WorkflowStatusReport>("/api/modon/review/workflow", Json))!;
        Assert.False(status.Ready);
        Assert.Contains("building-licence.bpmn", status.Summary);
        Assert.Equal([true, false], status.Camunda!.Processes.Select(p => p.Deployed));
        Assert.True(status.Camunda.ProcessDeployed); // the excavation process (top-level fields) is still deployed
    }

    [Fact]
    public async Task CamundaDown_RefusesStageActions_WithoutChangingAnything()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        Camunda.Down = true;

        await AssertProblem(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId),
            HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);

        Assert.Equal(RequestStatus.ConsultantSelection, (await Row(request.Id)).Status);
        Assert.Equal(0, await Count(db => db.BuildingTenders.Where(t => t.RequestId == request.Id)));
        Assert.Equal(2, await Count(db => db.RequestStatusHistory.Where(h => h.RequestId == request.Id)));
        Camunda.Down = false;
        AssertWaitsAt(request, "Gateway_ConsultantSelection");
    }

    [Fact]
    public async Task AStepAlreadyTakenInCamunda_RefusesAConflictingAction()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        Camunda.Deliver("BuildingConsultantTenderOpened", InstanceOf(request).CorrelationKey!); // someone else, out of band

        await AssertProblem(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId),
            HttpStatusCode.Conflict, ProblemCodes.InvalidTransition);

        Assert.Equal(RequestStatus.ConsultantSelection, (await Row(request.Id)).Status);
        var drift = await Check(request.Id);
        Assert.Equal((false, RequestStatus.ConsultantTender), (drift.InSync, drift.EngineStatus));
    }

    [Fact]
    public async Task ALostResponse_IsRecoveredOnRetry_WithOneRoundAndOneHistoryRow()
    {
        var (investor, request) = await _kit.Submitted();
        Camunda.LoseResponseOfNext.Add("messages/correlation");

        await AssertProblem(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender),
            HttpStatusCode.ServiceUnavailable, ProblemCodes.WorkflowUnavailable);
        Assert.Equal(RequestStatus.ConsultantSelection, (await Row(request.Id)).Status);
        AssertWaitsAt(request, "Gateway_ConsultantTender"); // Camunda did take the step

        var retried = await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.OpenTender));

        Assert.Equal(RequestStatus.ConsultantTender, retried.Status);
        Assert.Single(retried.Building!.Tenders);
        Assert.Equal(1, await Count(db => db.RequestStatusHistory.Where(h => h.RequestId == request.Id && h.Action == RequestAction.OpenConsultantTender)));
        Assert.True((await Check(request.Id)).InSync);
    }

    [Fact]
    public async Task SimultaneousAnswers_OneConsumedByCamunda_OneRefused()
    {
        var (investor, request) = await _kit.Submitted();
        var consultant = await _kit.Consultant();
        await Read(await Choose(investor.Client, request.Id, "consultant", SelectionMode.Direct, consultant.EstablishmentId));

        var results = await Task.WhenAll(
            Respond(consultant.Client, request.Id, ConsultantAccepts()),
            Respond(consultant.Client, request.Id, new OfficeResponseBody(OfficeDecision.Reject, null, "no")));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var row = await Row(request.Id);
        var instance = InstanceOf(request);
        Assert.Equal(row.Status == RequestStatus.ContractorSelection ? "Gateway_ContractorSelection" : "Gateway_ConsultantSelection", instance.CurrentElement);
        Assert.Equal(1, await Count(db => db.RequestStatusHistory.Where(h => h.RequestId == request.Id
            && (h.Action == RequestAction.ConsultantAccepted || h.Action == RequestAction.ConsultantDeclined))));
    }

    [Fact]
    public async Task Diagnostics_ListBothProcesses()
    {
        var status = (await (await _kit.Reviewer()).GetFromJsonAsync<WorkflowStatusReport>("/api/modon/review/workflow", Json))!;

        Assert.True(status.Ready);
        Assert.Equal(
            [(ExcavationProcess.DefaultProcessId, "excavation-permit.bpmn", true), (BuildingLicenceProcess.DefaultProcessId, "building-licence.bpmn", true)],
            status.Camunda!.Processes.Select(p => (p.ProcessId, p.BpmnFile, p.Deployed)));
        Assert.Contains("modon-building-licence' version 1", status.Summary);
        Assert.All(status.Camunda.Processes, p => Assert.Equal(p.CheckedInVersionTag, p.DeployedVersionTag));
    }
}
