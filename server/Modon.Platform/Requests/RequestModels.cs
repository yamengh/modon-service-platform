namespace Modon.Platform.Requests;

public sealed record CreateRequestBody(string ServiceCode);

public sealed record RequestSummary(
    long Id,
    string RequestNumber,
    string ServiceCode,
    string ServiceNameAr,
    string ServiceNameEn,
    RequestStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt);

public sealed record AttachmentInfo(string SlotCode, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt);

public sealed record HistoryItem(RequestStatus? FromStatus, RequestStatus ToStatus, RequestAction Action, DateTime OccurredAt, string? Comment);

public sealed record RequestDetail(
    long Id,
    string RequestNumber,
    string ServiceCode,
    string ServiceNameAr,
    string ServiceNameEn,
    RequestStatus Status,
    bool CanEdit,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    ExcavationDto Excavation,
    AttachmentSlot[] AttachmentSlots,
    AttachmentInfo[] Attachments,
    HistoryItem[] History,
    AttachmentSlot[] CoordinationLetterSlots,
    bool CanUploadCoordinationLetters,
    ApplicantTask? CurrentTask);

/// <summary>What Modon is waiting for the applicant to do.</summary>
public enum ApplicantTaskKind
{
    /// <summary>Edit the returned application and resubmit it.</summary>
    CompleteReturnedRequest,

    /// <summary>Upload the coordination letters and send them.</summary>
    UploadCoordinationLetters,
}

/// <summary>The open task on a request: the reviewer's instructions and when they were given.</summary>
public sealed record ApplicantTask(ApplicantTaskKind Kind, string? Instructions, DateTime AssignedAt);

/// <summary>A row of the applicant's task inbox.</summary>
public sealed record TaskItem(
    long RequestId,
    string RequestNumber,
    string ServiceCode,
    string ServiceNameAr,
    string ServiceNameEn,
    RequestStatus Status,
    ApplicantTaskKind Kind,
    string? Instructions,
    DateTime AssignedAt,
    DateTime? SubmittedAt);

public sealed record EstablishmentInfo(long Id, string CrNumber, string NameAr, string NameEn);

public static class RequestMapping
{
    public static AttachmentInfo ToInfo(this RequestAttachment a)
        => new(a.SlotCode, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt);

    /// <summary>Attachments ordered like the service's slots (as in the manual).</summary>
    public static AttachmentInfo[] InSlotOrder(this IEnumerable<RequestAttachment> attachments, AttachmentSlot[] slots)
        => attachments.OrderBy(a => Array.FindIndex(slots, s => s.Code == a.SlotCode)).Select(ToInfo).ToArray();

    /// <summary>
    /// The applicant's open task, if Modon is waiting for them. The instructions and date come from the
    /// history row that moved the request into its current status (the reviewer's return or letters request).
    /// </summary>
    public static ApplicantTask? CurrentTask(this ServiceRequest r, Workflow.IRequestWorkflow workflow)
    {
        if (!workflow.IsApplicantTask(r.Status)) return null;
        var entered = r.History.Where(h => h.ToStatus == r.Status)
            .OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).LastOrDefault();
        var kind = r.Status == RequestStatus.AwaitingCoordinationLetters
            ? ApplicantTaskKind.UploadCoordinationLetters
            : ApplicantTaskKind.CompleteReturnedRequest;
        return new ApplicantTask(kind, entered?.Comment, entered?.OccurredAt ?? r.UpdatedAt);
    }
}

/// <summary>Problem "title" values the portal recognises.</summary>
public static class ProblemCodes
{
    public const string ServiceNotOpen = "serviceNotOpen";
    public const string RoleNotAllowed = "roleNotAllowed";
    public const string RequestNotEditable = "requestNotEditable";
    public const string UnknownSlot = "unknownSlot";
    public const string MissingClientHeader = "missingClientHeader";
    public const string InvalidTransition = "invalidTransition";
    public const string RequestChanged = "requestChanged";
    public const string NotYourEstablishment = "notYourEstablishment";
    public const string WorkflowUnavailable = "workflowUnavailable";
    public const string WorkflowNotStarted = "workflowNotStarted";
}
