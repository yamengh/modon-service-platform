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

/// <summary>Problem "title" values the portal recognises.</summary>
public static class ProblemCodes
{
    public const string ServiceNotOpen = "serviceNotOpen";
    public const string RoleNotAllowed = "roleNotAllowed";
    public const string RequestNotEditable = "requestNotEditable";
    public const string UnknownSlot = "unknownSlot";
    public const string MissingClientHeader = "missingClientHeader";
}
