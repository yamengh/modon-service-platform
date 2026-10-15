namespace Modon.Platform.Requests.Workflow;

public sealed class InvalidTransitionException(RequestStatus from, RequestAction action)
    : Exception($"Action {action} is not allowed when the request is {from}.")
{
    public RequestStatus From { get; } = from;
    public RequestAction Action { get; } = action;
}

/// <summary>
/// Decides how a request's status changes. Milestone 2 uses <see cref="InProcessRequestWorkflow"/>;
/// the Camunda milestone adds an implementation that drives the same transitions through BPMN.
/// Callers never set <see cref="ServiceRequest.Status"/> directly.
/// </summary>
public interface IRequestWorkflow
{
    RequestStatus InitialStatus { get; }

    /// <summary>Whether the applicant may edit form data and attachments in this status.</summary>
    bool IsEditableByApplicant(RequestStatus status);

    /// <summary>Returns the next status, or throws <see cref="InvalidTransitionException"/>.</summary>
    Task<RequestStatus> NextStatusAsync(ServiceRequest request, RequestAction action, CancellationToken ct);
}

public sealed class InProcessRequestWorkflow : IRequestWorkflow
{
    private static readonly Dictionary<(RequestStatus, RequestAction), RequestStatus> Transitions = new()
    {
        [(RequestStatus.Draft, RequestAction.Submit)] = RequestStatus.InProgress,
    };

    private static readonly HashSet<RequestStatus> Editable = [RequestStatus.Draft];

    public RequestStatus InitialStatus => RequestStatus.Draft;

    public bool IsEditableByApplicant(RequestStatus status) => Editable.Contains(status);

    public Task<RequestStatus> NextStatusAsync(ServiceRequest request, RequestAction action, CancellationToken ct)
        => Transitions.TryGetValue((request.Status, action), out var next)
            ? Task.FromResult(next)
            : throw new InvalidTransitionException(request.Status, action);
}
