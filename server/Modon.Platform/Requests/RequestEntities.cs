using Modon.Platform.Organizations;

namespace Modon.Platform.Requests;

/// <summary>A request for any Modon service. Service-specific fields live in a 1:1 details table.</summary>
public class ServiceRequest
{
    public long Id { get; set; }

    /// <summary>Human-readable number, e.g. EXC-000001-15-10-26 (prefix-id-dd-MM-yy).</summary>
    public string RequestNumber { get; set; } = "";

    public string ServiceCode { get; set; } = "";
    public RequestStatus Status { get; set; }

    /// <summary>The FormCMS (ASP.NET Identity) user who owns the request.</summary>
    public string ApplicantUserId { get; set; } = "";

    /// <summary>The applicant's establishment (commercial registration). Optional while a draft.</summary>
    public long? EstablishmentId { get; set; }
    public Establishment? Establishment { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }

    // Link to the Camunda process instance (null for drafts and for requests handled by the in-process engine).
    // Set once, when the request is first submitted; resubmissions and letters continue the same instance.

    /// <summary>Camunda process instance key.</summary>
    public string? WorkflowInstanceKey { get; set; }

    /// <summary>Value of the instance's <c>correlationKey</c> variable; every message for this request uses it.</summary>
    public string? WorkflowCorrelationKey { get; set; }

    public string? WorkflowDefinitionKey { get; set; }
    public int? WorkflowDefinitionVersion { get; set; }

    public ExcavationPermitDetails? Excavation { get; set; }
    public Building.BuildingLicenceDetails? Building { get; set; }
    public List<RequestAttachment> Attachments { get; set; } = [];
    public List<RequestStatusHistory> History { get; set; } = [];
}

/// <summary>
/// One row per status change, including creation. A building licence's monthly progress reports also add a row
/// (from Approved to Approved), so the history shows every report next to the decisions.
/// </summary>
public class RequestStatusHistory
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public RequestStatus? FromStatus { get; set; }
    public RequestStatus ToStatus { get; set; }
    public RequestAction Action { get; set; }
    public string ActorUserId { get; set; } = "";
    public string? Comment { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>A document uploaded into one attachment slot of a request (one file per slot).</summary>
public class RequestAttachment
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public string SlotCode { get; set; } = "";

    /// <summary>Original file name, shown to the user and used for downloads only.</summary>
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>Key inside the private document store. Never derived from the user's file name.</summary>
    public string StorageKey { get; set; } = "";

    public string UploadedByUserId { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

/// <summary>Excavation works permit fields (تصريح أعمال الحفر), from the portal user manual.</summary>
public class ExcavationPermitDetails
{
    public long RequestId { get; set; }

    // تفاصيل الطلب — request details (codes refer to FormCMS "modonLookup" records)
    public string? RequestTypeCode { get; set; }
    public string? ContractTypeCode { get; set; }
    public string? IndustrialCityCode { get; set; }
    public string? PermitTypeCode { get; set; }
    public string? WorkDescription { get; set; }

    // تفاصيل التصريح — permit details
    public DateOnly? IssueDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? ExcavationMethodCode { get; set; }
    public string? WorkLocation { get; set; }

    // تفاصيل الأبعاد — dimensions in metres (optional)
    public decimal? LengthM { get; set; }
    public decimal? WidthM { get; set; }
    public decimal? DepthM { get; set; }
    public decimal? TotalPathLengthM { get; set; }

    // تفاصيل مدير المشروع — project manager
    public string? ProjectManagerName { get; set; }
    public string? ProjectManagerPhone { get; set; }
    public string? ProjectManagerEmail { get; set; }
    public string? SupervisingEntity { get; set; }
}
