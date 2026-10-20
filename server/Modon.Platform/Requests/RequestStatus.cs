namespace Modon.Platform.Requests;

/// <summary>
/// Lifecycle of a service request (stored by name, so new values need no migration):
/// <list type="bullet">
/// <item>Draft → InProgress (applicant submits)</item>
/// <item>InProgress → Approved / Rejected (reviewer; final)</item>
/// <item>InProgress → ReturnedForCompletion (reviewer) → InProgress (applicant resubmits)</item>
/// <item>InProgress → AwaitingCoordinationLetters (reviewer) → InProgress (applicant sends the letters)</item>
/// </list>
/// Building licence (Milestones 6–8, see <see cref="Building"/>):
/// <list type="bullet">
/// <item>Draft → ConsultantSelection (investor submits the basic information)</item>
/// <item>ConsultantSelection → ConsultantTender (open project) or AwaitingConsultantDecision (specific office)</item>
/// <item>ConsultantTender → ContractorSelection (investor picks a quotation) or back to ConsultantSelection (all rejected)</item>
/// <item>AwaitingConsultantDecision → ContractorSelection (office accepts) or back to ConsultantSelection (office rejects)</item>
/// <item>The contractor stage repeats this and ends in UnderModonReview → Approved / Rejected (reviewer; final)</item>
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

    // --- Building licence (رخصة البناء) -------------------------------------------------------

    /// <summary>مرحلة اختيار المكتب الاستشاري — the investor chooses an open project or a specific office.</summary>
    ConsultantSelection,

    /// <summary>تقديم عروض السعر من المكاتب — open project: consulting offices submit quotations.</summary>
    ConsultantTender,

    /// <summary>معلقة على قرار المكتب الاستشاري — a specific office must accept or reject.</summary>
    AwaitingConsultantDecision,

    /// <summary>مرحلة اختيار المقاول — the investor chooses an open project or a specific contractor.</summary>
    ContractorSelection,

    /// <summary>قيد إنتهاء مناقصة المقاول — open project: contractors submit quotations.</summary>
    ContractorTender,

    /// <summary>معلقة على قرار المقاول — a specific contractor must accept or reject.</summary>
    AwaitingContractorDecision,

    /// <summary>دراسة الطلب — with the industrial-city officer (Modon reviewer) for the licence decision.</summary>
    UnderModonReview,
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

    // --- Building licence. "Submit" (basic information), "Approve" and "Reject" are shared. ------

    /// <summary>Investor offers the project to all consulting offices (المشروع).</summary>
    OpenConsultantTender,

    /// <summary>Investor chooses a specific consulting office (اختيار مكتب استشاري محدد).</summary>
    SelectConsultant,

    /// <summary>Investor picks one consulting office's quotation (اختيار عرض سعر).</summary>
    AcceptConsultantQuotation,

    /// <summary>Investor rejects all quotations / cancels the project (رفض جميع العروض / إلغاء المشروع).</summary>
    RejectConsultantQuotations,

    /// <summary>The chosen consulting office accepts, with its quotation and phase plan.</summary>
    ConsultantAccepted,

    /// <summary>The chosen consulting office rejects, with a comment.</summary>
    ConsultantDeclined,

    OpenContractorTender,
    SelectContractor,
    AcceptContractorQuotation,
    RejectContractorQuotations,
    ContractorAccepted,
    ContractorDeclined,

    /// <summary>The selected consulting office submits a monthly construction-progress report (status unchanged).</summary>
    SubmitProgressReport,
}
