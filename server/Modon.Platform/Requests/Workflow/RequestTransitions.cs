using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;
using Modon.Platform.Notifications;

namespace Modon.Platform.Requests.Workflow;

/// <summary>
/// The only place that changes a request's status. It asks the workflow engine first, then adds exactly one
/// history row (actor, action, comment, time) and saves. <see cref="ServiceRequest.Status"/> is a concurrency
/// token, so if another user changed the status in the meantime the save fails instead of recording a second
/// transition; the engine step is then compensated where possible and the exception is rethrown.
/// The step's notifications are saved in the same transaction (so a refused or repeated action notifies nobody), and
/// handed to the other channels after the commit.
/// </summary>
public sealed class RequestTransitions(
    IRequestWorkflow workflow,
    ModonDbContext db,
    NotificationPlanner notifications,
    NotificationDispatcher dispatcher,
    TimeProvider clock,
    ILogger<RequestTransitions> logger)
{
    /// <param name="beforeSave">Extra changes saved together with the transition (e.g. the submission date).</param>
    /// <param name="officeNotices">Building licence: offices to notify about this step, besides the applicant.</param>
    public async Task<RequestStatusHistory> ApplyAndSaveAsync(
        ServiceRequest request, RequestAction action, string actorUserId, string? comment, CancellationToken ct,
        Action<ServiceRequest>? beforeSave = null, IReadOnlyList<OfficeNotice>? officeNotices = null)
    {
        WorkflowStep step;
        try
        {
            step = await workflow.AdvanceAsync(request, action, actorUserId, ct);
        }
        catch (WorkflowUnavailableException e)
        {
            logger.LogError("Request {Number}: {Action} refused, the {Engine} workflow engine is unavailable: {Reason}",
                request.RequestNumber, action, workflow.EngineName, e.Message);
            throw;
        }

        var entry = new RequestStatusHistory
        {
            RequestId = request.Id,
            FromStatus = request.Status,
            ToStatus = step.NextStatus,
            Action = action,
            ActorUserId = actorUserId,
            Comment = comment,
            OccurredAt = clock.GetUtcNow().UtcDateTime,
        };
        request.History.Add(entry);
        request.Status = step.NextStatus;
        request.UpdatedAt = entry.OccurredAt;
        if (step.Started is { } started)
        {
            request.WorkflowInstanceKey = started.ProcessInstanceKey;
            request.WorkflowCorrelationKey = started.CorrelationKey;
            request.WorkflowDefinitionKey = started.ProcessDefinitionKey;
            request.WorkflowDefinitionVersion = started.ProcessDefinitionVersion;
        }
        beforeSave?.Invoke(request);
        var planned = await notifications.PlanAsync(request, entry, officeNotices, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception saveError)
        {
            await CompensateQuietlyAsync(request, step, saveError);
            throw;
        }
        await DispatchQuietlyAsync(request, planned);
        return entry;
    }

    /// <summary>The step is saved; a channel failure must not turn it into an error (the retry service tries again).</summary>
    private async Task DispatchQuietlyAsync(ServiceRequest request, IReadOnlyList<ModonNotification> planned)
    {
        if (planned.Count == 0) return;
        try
        {
            await dispatcher.DispatchAsync(planned.Select(n => n.Id).ToArray(), CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Request {Number}: notifications saved but not yet delivered; they will be retried", request.RequestNumber);
        }
    }

    private async Task CompensateQuietlyAsync(ServiceRequest request, WorkflowStep step, Exception saveError)
    {
        if (saveError is not DbUpdateConcurrencyException)
            logger.LogError(saveError, "Request {Number}: saving {Status} failed after the workflow step", request.RequestNumber, step.NextStatus);
        try
        {
            await workflow.CompensateAsync(step, CancellationToken.None);
        }
        catch (Exception e)
        {
            // Not fatal: an orphan instance has its own correlation key, so it can never receive this request's messages.
            logger.LogError(e, "Request {Number}: could not undo the workflow step (process instance {Instance} may be left running)",
                request.RequestNumber, step.Started?.ProcessInstanceKey);
        }
    }
}
