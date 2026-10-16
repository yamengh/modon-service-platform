using Modon.Platform.Requests;

namespace Modon.Platform.Review;

/// <summary>The applicant as shown to reviewers (from the FormCMS / ASP.NET Identity user).</summary>
public sealed record ApplicantInfo(string UserId, string Email, string Name, string? Phone);

public sealed record ReviewQueueItem(
    long Id,
    string RequestNumber,
    string ServiceCode,
    string ServiceNameAr,
    string ServiceNameEn,
    RequestStatus Status,
    DateTime? SubmittedAt,
    DateTime UpdatedAt,
    string? IndustrialCityCode,
    ApplicantInfo Applicant,
    EstablishmentInfo? Establishment);

public sealed record ReviewQueuePage(ReviewQueueItem[] Items, int Total, int Page, int PageSize);

/// <summary>Number of reviewer-visible requests per status, keyed by camelCase status (drafts are never counted).</summary>
public sealed record ReviewSummary(int Total, Dictionary<string, int> ByStatus);

/// <summary>A status-history row with the actor resolved, for the audit view.</summary>
public sealed record ReviewHistoryItem(
    RequestStatus? FromStatus,
    RequestStatus ToStatus,
    RequestAction Action,
    DateTime OccurredAt,
    string? Comment,
    string ActorUserId,
    string? ActorEmail,
    bool ActorIsApplicant);

public sealed record ReviewRequestDetail(
    long Id,
    string RequestNumber,
    string ServiceCode,
    string ServiceNameAr,
    string ServiceNameEn,
    RequestStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    ApplicantInfo Applicant,
    EstablishmentInfo? Establishment,
    ExcavationDto Excavation,
    AttachmentSlot[] AttachmentSlots,
    AttachmentInfo[] Attachments,
    ReviewHistoryItem[] History,
    RequestAction[] AvailableDecisions);

/// <summary>Body for approve (optional note) and return-for-completion (required comment).</summary>
public sealed record CommentBody(string? Comment);

/// <summary>Body for reject (required reason).</summary>
public sealed record RejectBody(string? Reason);
