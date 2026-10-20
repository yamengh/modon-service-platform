namespace Modon.Platform.Requests.Building;

/// <summary>Building licence form data as sent and received by the portal. Every field may be empty in a draft.</summary>
public sealed record BuildingLicenceDto(
    long? EstablishmentId = null,
    string? ContractNumber = null,
    string? LicenceTypeCode = null,
    decimal? ConstructionBudgetSar = null,
    int? FloorCount = null,
    string? ScopeOfWork = null,
    string? Notes = null,
    BuildingContactDto[]? Contacts = null,
    bool TermsAccepted = false)
{
    public static BuildingLicenceDto From(ServiceRequest request)
    {
        var d = request.Building ?? new BuildingLicenceDetails();
        return new BuildingLicenceDto(
            request.EstablishmentId, d.ContractNumber, d.LicenceTypeCode, d.ConstructionBudgetSar, d.FloorCount,
            d.ScopeOfWork, d.Notes,
            d.Contacts.OrderBy(c => c.SortOrder).Select(c => new BuildingContactDto(c.Name, c.Email, c.Mobile)).ToArray(),
            d.TermsAccepted);
    }

    /// <summary>Trims text and turns blank strings into null; drops completely empty contact rows.</summary>
    public BuildingLicenceDto Normalized() => this with
    {
        ContractNumber = Clean(ContractNumber),
        LicenceTypeCode = Clean(LicenceTypeCode),
        ScopeOfWork = Clean(ScopeOfWork),
        Notes = Clean(Notes),
        Contacts = (Contacts ?? [])
            .Select(c => new BuildingContactDto(Clean(c.Name), Clean(c.Email), Clean(c.Mobile)))
            .Where(c => c.Name is not null || c.Email is not null || c.Mobile is not null)
            .ToArray(),
    };

    public void ApplyTo(ServiceRequest request)
    {
        request.EstablishmentId = EstablishmentId;
        var d = request.Building ??= new BuildingLicenceDetails();
        d.ContractNumber = ContractNumber;
        d.LicenceTypeCode = LicenceTypeCode;
        d.ConstructionBudgetSar = ConstructionBudgetSar;
        d.FloorCount = FloorCount;
        d.ScopeOfWork = ScopeOfWork;
        d.Notes = Notes;
        d.TermsAccepted = TermsAccepted;
        d.Contacts.Clear();
        var order = 0;
        foreach (var c in Contacts ?? [])
            d.Contacts.Add(new BuildingContact { SortOrder = order++, Name = c.Name ?? "", Email = c.Email ?? "", Mobile = c.Mobile ?? "" });
    }

    internal static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>A contact person (تفاصيل الفرد): name, e-mail, mobile.</summary>
public sealed record BuildingContactDto(string? Name, string? Email, string? Mobile);

// --- investor actions ------------------------------------------------------------------------------

/// <summary>Investor's choice for a stage: an open project, or a specific office (<paramref name="EstablishmentId"/>).</summary>
public sealed record SelectionBody(SelectionMode? Mode, long? EstablishmentId);

/// <summary>Optional comment of the investor (e.g. why all quotations were rejected).</summary>
public sealed record InvestorCommentBody(string? Comment);

// --- office actions ---------------------------------------------------------------------------------

/// <summary>A quotation for an open project: which of the user's offices, the amount in riyals and a comment.</summary>
public sealed record QuotationBody(long? EstablishmentId, decimal? AmountSar, string? Comment);

public enum OfficeDecision
{
    Accept,
    Reject,
}

/// <summary>
/// An invited office's answer (manual 1.4 for consulting offices, 1.7 for contractors). Accepting needs the amount;
/// a consulting office also gives its phase table and confirms its commitments. Rejecting needs a comment.
/// Sent as JSON, or as multipart form data ("data" = this JSON, "file" = the optional attachment).
/// </summary>
public sealed record OfficeResponseBody(
    OfficeDecision? Decision,
    decimal? AmountSar,
    string? Comment,
    bool CommitmentsAccepted = false,
    PhasePlanDto[]? Phases = null);

public sealed record PhasePlanDto(ConstructionPhase Phase, decimal? SharePercent, int? DurationDays);

/// <summary>A monthly progress report: "yyyy-MM", the completion of all six phases (0–100 each), a comment.</summary>
public sealed record ProgressReportBody(string? Period, PhaseProgressDto[]? Phases, string? Comment);

public sealed record PhaseProgressDto(ConstructionPhase Phase, decimal? CompletionPercent);

// --- views ------------------------------------------------------------------------------------------

/// <summary>A consulting office or contractor (its commercial registration).</summary>
public sealed record OfficeInfo(long Id, string CrNumber, string NameAr, string NameEn);

public sealed record StoredFile(string FileName, string ContentType, long SizeBytes);

public sealed record QuotationView(
    long Id,
    long TenderId,
    BuildingStage Stage,
    OfficeInfo Office,
    QuotationState State,
    decimal? AmountSar,
    string? Comment,
    bool CommitmentsAccepted,
    DateTime SubmittedAt,
    StoredFile? Attachment,
    PhasePlanDto[] Phases);

public sealed record TenderView(
    long Id,
    BuildingStage Stage,
    SelectionMode Mode,
    string? Number,
    OfficeInfo? InvitedOffice,
    TenderState State,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    QuotationView[] Quotations);

public sealed record ProgressReportView(
    long Id,
    string Period,
    OfficeInfo Office,
    string SubmittedByUserId,
    string? SubmittedByEmail,
    DateTime SubmittedAt,
    string? Comment,
    StoredFile? Attachment,
    PhaseProgressDto[] Phases);

/// <summary>Everything about a building licence beyond the shared request fields (investor and reviewer views).</summary>
/// <param name="InvestorActions">Stage actions the investor can take now (empty for reviewers).</param>
/// <param name="CurrentProgress">Latest completion per phase (from the most recent report), all six phases.</param>
public sealed record BuildingView(
    BuildingLicenceDto Form,
    TenderView[] Tenders,
    OfficeInfo? SelectedConsultant,
    QuotationView? ConsultantQuotation,
    OfficeInfo? SelectedContractor,
    QuotationView? ContractorQuotation,
    ProgressReportView[] ProgressReports,
    PhaseProgressDto[] CurrentProgress,
    RequestAction[] InvestorActions);

/// <summary>How an office relates to a building project.</summary>
public enum PartnerRelation
{
    /// <summary>المشاريع المطروحة — an open project the office may quote for.</summary>
    OpenTender,

    /// <summary>The investor chose this office directly; it must accept or reject.</summary>
    Invited,

    /// <summary>المشاريع الراسية — the office was selected for the project.</summary>
    Awarded,
}

/// <summary>A row of the office's project list.</summary>
public sealed record PartnerProjectSummary(
    long RequestId,
    string RequestNumber,
    string? ProjectNumber,
    RequestStatus Status,
    BuildingStage Stage,
    PartnerRelation Relation,
    string? LicenceTypeCode,
    OfficeInfo? Investor,
    DateTime Since,
    bool Answered);

/// <summary>
/// What an office may see of a building project: the basic information (no contacts) and, once it is invited or
/// selected, the application's plans. Quotations of other offices are never shown.
/// </summary>
public sealed record PartnerProjectDetail(
    long RequestId,
    string RequestNumber,
    RequestStatus Status,
    PartnerRelation Relation,
    BuildingStage Stage,
    TenderView? CurrentRound,
    OfficeInfo? Investor,
    string? LicenceTypeCode,
    decimal? ConstructionBudgetSar,
    int? FloorCount,
    string? ScopeOfWork,
    string? Notes,
    DateTime? SubmittedAt,
    OfficeInfo[] MyOffices,
    QuotationView[] MyQuotations,
    AttachmentSlot[] AttachmentSlots,
    AttachmentInfo[] Attachments,
    QuotationView? ConsultantPlan,
    ProgressReportView[] ProgressReports,
    PhaseProgressDto[] CurrentProgress,
    bool CanQuote,
    bool CanRespond,
    bool CanReport);
