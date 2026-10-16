namespace Modon.Platform.Requests.Workflow;

public sealed class InvalidTransitionException(RequestStatus from, RequestAction action)
    : Exception($"Action {action} is not allowed when the request is {from}.")
{
    public RequestStatus From { get; } = from;
    public RequestAction Action { get; } = action;
}

/// <summary>
/// Decides how a request's status changes. This milestone uses <see cref="InProcessRequestWorkflow"/>;
/// the Camunda milestone adds an implementation that drives the same transitions through BPMN.
/// Callers never set <see cref="ServiceRequest.Status"/> directly — they go through
/// <see cref="RequestTransitions"/>, which asks the workflow and writes the history row.
/// </summary>
public interface IRequestWorkflow
{
    RequestStatus InitialStatus { get; }

    /// <summary>Whether the applicant may edit form data and attachments in this status.</summary>
    bool IsEditableByApplicant(RequestStatus status);

    /// <summary>Whether Modon reviewers can see requests in this status (drafts stay private).</summary>
    bool IsVisibleToReviewers(RequestStatus status);

    /// <summary>Reviewer decisions that are legal from this status.</summary>
    IReadOnlyList<RequestAction> ReviewActions(RequestStatus status);

    /// <summary>Returns the next status, or throws <see cref="InvalidTransitionException"/>.</summary>
    Task<RequestStatus> NextStatusAsync(ServiceRequest request, RequestAction action, CancellationToken ct);
}

public sealed class InProcessRequestWorkflow : IRequestWorkflow
{
    private static readonly Dictionary<(RequestStatus, RequestAction), RequestStatus> Transitions = new()
    {
        [(RequestStatus.Draft, RequestAction.Submit)] = RequestStatus.InProgress,
        [(RequestStatus.InProgress, RequestAction.Approve)] = RequestStatus.Approved,
        [(RequestStatus.InProgress, RequestAction.Reject)] = RequestStatus.Rejected,
        [(RequestStatus.InProgress, RequestAction.ReturnForCompletion)] = RequestStatus.ReturnedForCompletion,
    };

    private static readonly RequestAction[] ReviewerActions =
        [RequestAction.Approve, RequestAction.ReturnForCompletion, RequestAction.Reject];

    private static readonly HashSet<RequestStatus> Editable = [RequestStatus.Draft];

    public RequestStatus InitialStatus => RequestStatus.Draft;

    public bool IsEditableByApplicant(RequestStatus status) => Editable.Contains(status);

    public bool IsVisibleToReviewers(RequestStatus status) => status != RequestStatus.Draft;

    public IReadOnlyList<RequestAction> ReviewActions(RequestStatus status)
        => ReviewerActions.Where(a => Transitions.ContainsKey((status, a))).ToArray();

    public Task<RequestStatus> NextStatusAsync(ServiceRequest request, RequestAction action, CancellationToken ct)
        => Transitions.TryGetValue((request.Status, action), out var next)
            ? Task.FromResult(next)
            : throw new InvalidTransitionException(request.Status, action);
}
