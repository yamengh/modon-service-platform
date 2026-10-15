namespace Modon.Platform.Requests;

/// <summary>
/// Lifecycle of a service request. Milestone 2 implements Draft → InProgress;
/// the other states are reserved for the review and workflow milestones.
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
}
