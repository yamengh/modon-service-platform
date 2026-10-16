namespace Modon.Platform.Requests;

/// <summary>
/// Lifecycle of a service request. Implemented so far: Draft → InProgress (applicant submits) and
/// InProgress → Approved / Rejected / ReturnedForCompletion (reviewer decides). Resubmitting a returned
/// request and cancellation belong to later milestones.
/// </summary>
public enum RequestStatus
{
    /// <summary>مسودة</summary>
    Draft,

    /// <summary>قيد التنفيذ — submitted and waiting for Modon's review.</summary>
    InProgress,

    /// <summary>إكمال النواقص — returned by Modon for the applicant to complete.</summary>
    ReturnedForCompletion,

    Approved,
    Rejected,
    Cancelled,
}

/// <summary>Something that changes a request's status. Every action is written to the status history.</summary>
public enum RequestAction
{
    Create,
    Submit,

    // Reviewer decisions (Milestone 3)
    Approve,
    Reject,
    ReturnForCompletion,
}
