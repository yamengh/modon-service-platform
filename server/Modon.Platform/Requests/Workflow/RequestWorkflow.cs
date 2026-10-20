using Modon.Platform.Catalog;

namespace Modon.Platform.Requests.Workflow;

public sealed class InvalidTransitionException(RequestStatus from, RequestAction action)
    : Exception($"Action {action} is not allowed when the request is {from}.")
{
    public RequestStatus From { get; } = from;
    public RequestAction Action { get; } = action;
}

/// <summary>
/// Decides how a request's status changes. Two implementations exist: <see cref="InProcessRequestWorkflow"/>
/// (rules only; used by the automated tests and as the local fallback) and
/// <see cref="Camunda.CamundaRequestWorkflow"/> (the same rules, driven through each service's BPMN in Camunda 8).
/// The rules are per service: the excavation permit and the building licence have separate lifecycles.
/// Callers never set <see cref="ServiceRequest.Status"/> directly — they go through
/// <see cref="RequestTransitions"/>, which asks the workflow, writes the history row and saves.
/// </summary>
public interface IRequestWorkflow
{
    /// <summary>"inProcess" or "camunda": shown in diagnostics.</summary>
    string EngineName { get; }

    RequestStatus InitialStatus { get; }

    /// <summary>Whether the applicant may edit the application form and its attachments in this status.</summary>
    bool IsEditableByApplicant(RequestStatus status);

    /// <summary>Whether the applicant may upload, replace or remove coordination letters in this status.</summary>
    bool AcceptsCoordinationLetters(RequestStatus status);

    /// <summary>Whether Modon reviewers can see requests in this status (drafts stay private).</summary>
    bool IsVisibleToReviewers(RequestStatus status);

    /// <summary>Reviewer decisions that are legal from this status.</summary>
    IReadOnlyList<RequestAction> ReviewActions(RequestStatus status);

    /// <summary>
    /// Statuses in which Modon is waiting for the applicant (returned, awaiting letters): these requests
    /// appear in the applicant's task inbox. A draft is the applicant's own work, not a task.
    /// </summary>
    bool IsApplicantTask(RequestStatus status);

    /// <summary>The action that sends the edited application for review: Submit for a draft, Resubmit for a returned request.</summary>
    RequestAction? ApplicantSubmitAction(RequestStatus status);

    /// <summary>
    /// Performs <paramref name="action"/> in the workflow engine and returns the next status. Throws
    /// <see cref="InvalidTransitionException"/> when the action is not allowed now, and
    /// <see cref="WorkflowUnavailableException"/> / <see cref="WorkflowNotStartedException"/> for engine problems.
    /// Nothing is written to the database here; <see cref="RequestTransitions"/> does that afterwards.
    /// </summary>
    Task<WorkflowStep> AdvanceAsync(ServiceRequest request, RequestAction action, string actorUserId, CancellationToken ct);

    /// <summary>
    /// Called when saving a step to the database failed (for example another user changed the request first).
    /// Undoes what the engine can undo — a process instance that was just started is cancelled.
    /// </summary>
    Task CompensateAsync(WorkflowStep step, CancellationToken ct);
}

/// <summary>The outcome of <see cref="IRequestWorkflow.AdvanceAsync"/>.</summary>
/// <param name="Started">Set when this step started a process instance; it is stored on the request.</param>
public sealed record WorkflowStep(RequestStatus NextStatus, WorkflowInstanceLink? Started = null);

/// <summary>Which engine process instance a request is linked to (persisted on <see cref="ServiceRequest"/>).</summary>
public sealed record WorkflowInstanceLink(
    string ProcessInstanceKey,
    string CorrelationKey,
    string ProcessDefinitionKey,
    int ProcessDefinitionVersion);

/// <summary>The workflow engine could not be reached or refused the command for a technical reason.</summary>
public sealed class WorkflowUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The request has no process instance in the engine (for example it was submitted while the in-process engine was active).</summary>
public sealed class WorkflowNotStartedException(string requestNumber)
    : Exception($"Request {requestNumber} is not linked to a workflow process instance.");

public sealed class InProcessRequestWorkflow : IRequestWorkflow
{
    /// <summary>Excavation works permit (Milestones 2–4).</summary>
    private static readonly Dictionary<(RequestStatus, RequestAction), RequestStatus> Excavation = new()
    {
        // Applicant
        [(RequestStatus.Draft, RequestAction.Submit)] = RequestStatus.InProgress,
        [(RequestStatus.ReturnedForCompletion, RequestAction.Resubmit)] = RequestStatus.InProgress,
        [(RequestStatus.AwaitingCoordinationLetters, RequestAction.SubmitCoordinationLetters)] = RequestStatus.InProgress,

        // Reviewer (Approved and Rejected have no way out: decisions are final)
        [(RequestStatus.InProgress, RequestAction.Approve)] = RequestStatus.Approved,
        [(RequestStatus.InProgress, RequestAction.Reject)] = RequestStatus.Rejected,
        [(RequestStatus.InProgress, RequestAction.ReturnForCompletion)] = RequestStatus.ReturnedForCompletion,
        [(RequestStatus.InProgress, RequestAction.RequestCoordinationLetters)] = RequestStatus.AwaitingCoordinationLetters,
    };

    /// <summary>Building licence (Milestones 6–8), from the building-licence user manual (January 2026).</summary>
    private static readonly Dictionary<(RequestStatus, RequestAction), RequestStatus> Building = new()
    {
        // Investor: basic information, then the consulting office (open project or a specific office)
        [(RequestStatus.Draft, RequestAction.Submit)] = RequestStatus.ConsultantSelection,
        [(RequestStatus.ConsultantSelection, RequestAction.OpenConsultantTender)] = RequestStatus.ConsultantTender,
        [(RequestStatus.ConsultantSelection, RequestAction.SelectConsultant)] = RequestStatus.AwaitingConsultantDecision,
        [(RequestStatus.ConsultantTender, RequestAction.AcceptConsultantQuotation)] = RequestStatus.ContractorSelection,
        [(RequestStatus.ConsultantTender, RequestAction.RejectConsultantQuotations)] = RequestStatus.ConsultantSelection,
        // The specific office answers; a rejection sends the investor back to choosing (manual 1.4.5)
        [(RequestStatus.AwaitingConsultantDecision, RequestAction.ConsultantAccepted)] = RequestStatus.ContractorSelection,
        [(RequestStatus.AwaitingConsultantDecision, RequestAction.ConsultantDeclined)] = RequestStatus.ConsultantSelection,

        // Contractor stage, same shape; it ends with the industrial-city officer (manual 1.7.5)
        [(RequestStatus.ContractorSelection, RequestAction.OpenContractorTender)] = RequestStatus.ContractorTender,
        [(RequestStatus.ContractorSelection, RequestAction.SelectContractor)] = RequestStatus.AwaitingContractorDecision,
        [(RequestStatus.ContractorTender, RequestAction.AcceptContractorQuotation)] = RequestStatus.UnderModonReview,
        [(RequestStatus.ContractorTender, RequestAction.RejectContractorQuotations)] = RequestStatus.ContractorSelection,
        [(RequestStatus.AwaitingContractorDecision, RequestAction.ContractorAccepted)] = RequestStatus.UnderModonReview,
        [(RequestStatus.AwaitingContractorDecision, RequestAction.ContractorDeclined)] = RequestStatus.ContractorSelection,

        // Reviewer (final), then monthly progress reports while the licence is approved
        [(RequestStatus.UnderModonReview, RequestAction.Approve)] = RequestStatus.Approved,
        [(RequestStatus.UnderModonReview, RequestAction.Reject)] = RequestStatus.Rejected,
        [(RequestStatus.Approved, RequestAction.SubmitProgressReport)] = RequestStatus.Approved,
    };

    private static readonly Dictionary<string, Dictionary<(RequestStatus, RequestAction), RequestStatus>> ByService = new()
    {
        [ServiceCatalogData.ExcavationPermitCode] = Excavation,
        [ServiceCatalogData.BuildingLicenceCode] = Building,
    };

    private static readonly RequestAction[] ReviewerActions =
    [
        RequestAction.Approve, RequestAction.RequestCoordinationLetters,
        RequestAction.ReturnForCompletion, RequestAction.Reject,
    ];

    private static readonly HashSet<RequestStatus> Editable = [RequestStatus.Draft, RequestStatus.ReturnedForCompletion];

    public string EngineName => "inProcess";

    public RequestStatus InitialStatus => RequestStatus.Draft;

    public bool IsEditableByApplicant(RequestStatus status) => Editable.Contains(status);

    public bool AcceptsCoordinationLetters(RequestStatus status) => status == RequestStatus.AwaitingCoordinationLetters;

    public bool IsVisibleToReviewers(RequestStatus status) => status != RequestStatus.Draft;

    /// <summary>Apart from Draft and the final statuses, each service has its own statuses, so the status alone decides.</summary>
    public IReadOnlyList<RequestAction> ReviewActions(RequestStatus status)
        => ReviewerActions.Where(a => ByService.Values.Any(t => t.ContainsKey((status, a)))).ToArray();

    public bool IsApplicantTask(RequestStatus status)
        => status is RequestStatus.ReturnedForCompletion or RequestStatus.AwaitingCoordinationLetters;

    public RequestAction? ApplicantSubmitAction(RequestStatus status) => status switch
    {
        RequestStatus.Draft => RequestAction.Submit,
        RequestStatus.ReturnedForCompletion => RequestAction.Resubmit,
        _ => null,
    };

    /// <summary>The excavation lifecycle's next status (kept for the excavation contract tests).</summary>
    public RequestStatus NextStatus(RequestStatus from, RequestAction action)
        => NextStatus(ServiceCatalogData.ExcavationPermitCode, from, action);

    /// <summary>The next status for <paramref name="action"/> in the service's lifecycle, or throws <see cref="InvalidTransitionException"/>.</summary>
    public RequestStatus NextStatus(string serviceCode, RequestStatus from, RequestAction action)
        => ByService.TryGetValue(serviceCode, out var table) && table.TryGetValue((from, action), out var next)
            ? next
            : throw new InvalidTransitionException(from, action);

    /// <summary>Whether <paramref name="action"/> is legal now (no exception).</summary>
    public bool Allows(string serviceCode, RequestStatus from, RequestAction action)
        => ByService.TryGetValue(serviceCode, out var table) && table.ContainsKey((from, action));

    public Task<WorkflowStep> AdvanceAsync(ServiceRequest request, RequestAction action, string actorUserId, CancellationToken ct)
        => Task.FromResult(new WorkflowStep(NextStatus(request.ServiceCode, request.Status, action)));

    public Task CompensateAsync(WorkflowStep step, CancellationToken ct) => Task.CompletedTask;
}
