namespace Modon.Platform.Requests.Workflow;

/// <summary>
/// The only place that changes a request's status. It asks the workflow for the next status and adds
/// exactly one history row (actor, action, comment, time) for the change. The caller saves the context;
/// <see cref="ServiceRequest.Status"/> is a concurrency token, so if another user changed the status in
/// the meantime the save fails instead of recording a second transition.
/// </summary>
public sealed class RequestTransitions(IRequestWorkflow workflow, TimeProvider clock)
{
    public async Task<RequestStatusHistory> ApplyAsync(
        ServiceRequest request, RequestAction action, string actorUserId, string? comment, CancellationToken ct)
    {
        var next = await workflow.NextStatusAsync(request, action, ct);
        var entry = new RequestStatusHistory
        {
            RequestId = request.Id,
            FromStatus = request.Status,
            ToStatus = next,
            Action = action,
            ActorUserId = actorUserId,
            Comment = comment,
            OccurredAt = clock.GetUtcNow().UtcDateTime,
        };
        request.History.Add(entry);
        request.Status = next;
        request.UpdatedAt = entry.OccurredAt;
        return entry;
    }
}
