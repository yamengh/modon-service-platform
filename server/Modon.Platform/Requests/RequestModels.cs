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
    HistoryItem[] History);

public sealed record EstablishmentInfo(long Id, string CrNumber, string NameAr, string NameEn);

public static class RequestMapping
{
    public static AttachmentInfo ToInfo(this RequestAttachment a)
        => new(a.SlotCode, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt);

    /// <summary>Attachments ordered like the service's slots (as in the manual).</summary>
    public static AttachmentInfo[] InSlotOrder(this IEnumerable<RequestAttachment> attachments, AttachmentSlot[] slots)
        => attachments.OrderBy(a => Array.FindIndex(slots, s => s.Code == a.SlotCode)).Select(ToInfo).ToArray();
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
}
