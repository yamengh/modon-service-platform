using System.Text.Json;
using Modon.Platform.Catalog;

namespace Modon.Platform.Requests.Workflow.Camunda;

/// <summary>
/// Drives the request lifecycle through the excavation BPMN in Camunda 8.
/// <list type="bullet">
/// <item>The rules (who may do what, in which status) are the in-process ones; an illegal action is refused
/// before Camunda is called.</item>
/// <item>First submission starts a process instance; the instance key and a per-request correlation key are
/// stored on the request. Every later action correlates one message to that instance.</item>
/// <item>Camunda is asked first, the database is written second (by <see cref="RequestTransitions"/>).
/// The BPMN waits at an event-based gateway, so of two conflicting or repeated actions only one message is
/// accepted; the other finds no subscription and is refused (409) — one outcome, one history row.</item>
/// </list>
/// </summary>
public sealed class CamundaRequestWorkflow(
    InProcessRequestWorkflow rules,
    ICamundaEngine engine,
    ILogger<CamundaRequestWorkflow> logger) : IRequestWorkflow
{
    /// <summary>
    /// How far a Camunda element's start time may be before the request's last database update and still
    /// count as "a step the database missed" (clock difference between Modon and Camunda).
    /// </summary>
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(2);

    public string EngineName => "camunda";
    public RequestStatus InitialStatus => rules.InitialStatus;
    public bool IsEditableByApplicant(RequestStatus status) => rules.IsEditableByApplicant(status);
    public bool AcceptsCoordinationLetters(RequestStatus status) => rules.AcceptsCoordinationLetters(status);
    public bool IsVisibleToReviewers(RequestStatus status) => rules.IsVisibleToReviewers(status);
    public IReadOnlyList<RequestAction> ReviewActions(RequestStatus status) => rules.ReviewActions(status);
    public bool IsApplicantTask(RequestStatus status) => rules.IsApplicantTask(status);
    public RequestAction? ApplicantSubmitAction(RequestStatus status) => rules.ApplicantSubmitAction(status);

    public async Task<WorkflowStep> AdvanceAsync(ServiceRequest request, RequestAction action, string actorUserId, CancellationToken ct)
    {
        var next = rules.NextStatus(request.Status, action); // illegal actions never reach Camunda
        if (request.ServiceCode != ServiceCatalogData.ExcavationPermitCode)
            throw new WorkflowUnavailableException($"No Camunda process is configured for service '{request.ServiceCode}'.");

        if (action == RequestAction.Submit) return new WorkflowStep(next, await StartAsync(request, actorUserId, ct));

        if (request.WorkflowInstanceKey is not { } instanceKey || request.WorkflowCorrelationKey is not { } correlationKey)
            throw new WorkflowNotStartedException(request.RequestNumber);

        var message = ExcavationProcess.Messages[action];
        var variables = new Dictionary<string, object?>
        {
            ["lastAction"] = JsonNamingPolicy.CamelCase.ConvertName(action.ToString()),
            ["lastActorUserId"] = actorUserId,
        };
        var deliveredTo = await engine.CorrelateAsync(message, correlationKey, variables, ct);
        if (deliveredTo is not null)
        {
            if (deliveredTo != instanceKey)
            {
                // The correlation key is unique per request, so this means the data was changed by hand.
                logger.LogError("Request {Number}: message {Message} went to instance {Actual}, expected {Expected}",
                    request.RequestNumber, message, deliveredTo, instanceKey);
                throw new WorkflowUnavailableException(
                    $"Camunda delivered {message} to process instance {deliveredTo}, but request {request.RequestNumber} is linked to {instanceKey}.");
            }
            return new WorkflowStep(next);
        }

        // Nobody was waiting for this message. Usually the step is not possible in the process now (someone else
        // acted first) — but it can also mean an earlier attempt reached Camunda and then failed to save in the
        // database. In that case Camunda is already where this action leads, and the database may catch up.
        var view = await engine.GetInstanceAsync(instanceKey, ct);
        if (view.Status == next && view.ElementSince >= request.UpdatedAt - ClockTolerance)
        {
            logger.LogWarning("Request {Number}: Camunda already performed {Action} (instance {Instance} at {Element}); saving it now",
                request.RequestNumber, action, instanceKey, view.ElementId);
            return new WorkflowStep(next);
        }

        logger.LogInformation("Request {Number}: {Action} refused, Camunda instance {Instance} is at {Element} ({State})",
            request.RequestNumber, action, instanceKey, view.ElementId ?? "-", view.State);
        throw new InvalidTransitionException(request.Status, action);
    }

    public async Task CompensateAsync(WorkflowStep step, CancellationToken ct)
    {
        if (step.Started is not { } started) return; // a delivered message cannot be taken back
        await engine.CancelAsync(started.ProcessInstanceKey, ct);
        logger.LogWarning("Cancelled process instance {Instance}: its request was not saved", started.ProcessInstanceKey);
    }

    private Task<WorkflowInstanceLink> StartAsync(ServiceRequest request, string actorUserId, CancellationToken ct)
    {
        // A fresh key per submission: if this attempt fails and leaves an instance behind, that orphan can
        // never receive this request's messages.
        var correlationKey = $"{request.RequestNumber}:{Guid.NewGuid():N}";
        var variables = new Dictionary<string, object?>
        {
            ["requestId"] = request.Id,
            ["requestNumber"] = request.RequestNumber,
            ["serviceCode"] = request.ServiceCode,
            [ExcavationProcess.CorrelationKeyVariable] = correlationKey,
            ["submittedByUserId"] = actorUserId,
        };
        return engine.StartAsync(request.RequestNumber, correlationKey, variables, ct);
    }
}
