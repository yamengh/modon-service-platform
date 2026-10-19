using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;

namespace Modon.Platform.Requests.Workflow;

/// <summary>
/// The only place that changes a request's status. It asks the workflow engine first, then adds exactly one
/// history row (actor, action, comment, time) and saves. <see cref="ServiceRequest.Status"/> is a concurrency
/// token, so if another user changed the status in the meantime the save fails instead of recording a second
/// transition; the engine step is then compensated where possible and the exception is rethrown.
/// </summary>
public sealed class RequestTransitions(
    IRequestWorkflow workflow,
    ModonDbContext db,
    TimeProvider clock,
    ILogger<RequestTransitions> logger)
{
    /// <param name="beforeSave">Extra changes saved together with the transition (e.g. the submission date).</param>
    public async Task<RequestStatusHistory> ApplyAndSaveAsync(
        ServiceRequest request, RequestAction action, string actorUserId, string? comment, CancellationToken ct,
        Action<ServiceRequest>? beforeSave = null)
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception saveError)
        {
            await CompensateQuietlyAsync(request, step, saveError);
            throw;
        }
        return entry;
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
