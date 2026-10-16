namespace Modon.Platform.Requests;

/// <summary>
/// Lifecycle of a service request (stored by name, so new values need no migration):
/// <list type="bullet">
/// <item>Draft → InProgress (applicant submits)</item>
/// <item>InProgress → Approved / Rejected (reviewer; final)</item>
/// <item>InProgress → ReturnedForCompletion (reviewer) → InProgress (applicant resubmits)</item>
/// <item>InProgress → AwaitingCoordinationLetters (reviewer) → InProgress (applicant sends the letters)</item>
/// </list>
/// The transitions themselves live in <see cref="Workflow.InProcessRequestWorkflow"/>.
/// Cancellation belongs to a later milestone.
/// </summary>
public enum RequestStatus
{
    /// <summary>مسودة</summary>
    Draft,

    /// <summary>قيد التنفيذ — submitted and waiting for Modon's review.</summary>
    InProgress,

    /// <summary>إكمال النواقص — returned by Modon for the applicant to complete and resubmit.</summary>
    ReturnedForCompletion,

    /// <summary>بانتظار خطابات التنسيق — Modon asked the applicant for the coordination letters.</summary>
    AwaitingCoordinationLetters,

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

    // Milestone 4
    /// <summary>The applicant sends a returned request back for review.</summary>
    Resubmit,

    /// <summary>A reviewer asks the applicant for the coordination letters.</summary>
    RequestCoordinationLetters,

    /// <summary>The applicant sends the requested coordination letters.</summary>
    SubmitCoordinationLetters,
}
