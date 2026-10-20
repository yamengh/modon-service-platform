namespace Modon.Platform.Requests.Building;

/// <summary>
/// Building licence fields (طلب رخصة بناء), from the building-licence user manual (January 2026, section 1.1).
/// The commercial registration is <see cref="ServiceRequest.EstablishmentId"/>.
/// </summary>
public class BuildingLicenceDetails
{
    public long RequestId { get; set; }

    /// <summary>رقم العقد / رقم التسجيل — a sub-lease contract or a private-city registration number.</summary>
    public string? ContractNumber { get; set; }

    /// <summary>نوع الرخصة — a code from <see cref="BuildingLicence.LicenceTypes"/>.</summary>
    public string? LicenceTypeCode { get; set; }

    /// <summary>ميزانية الإنشاء, in Saudi riyals.</summary>
    public decimal? ConstructionBudgetSar { get; set; }

    /// <summary>عدد الأدوار.</summary>
    public int? FloorCount { get; set; }

    /// <summary>إطار العمل (optional in the manual).</summary>
    public string? ScopeOfWork { get; set; }

    /// <summary>ملاحظات إضافية (optional).</summary>
    public string? Notes { get; set; }

    /// <summary>"للتحقق من الأحكام والشروط" — required to submit.</summary>
    public bool TermsAccepted { get; set; }

    // The offices chosen in the consultant and contractor stages (their quotation is the Selected one of the
    // stage's awarded round). The selected consulting office submits the monthly progress reports.
    public long? SelectedConsultantEstablishmentId { get; set; }
    public long? SelectedContractorEstablishmentId { get; set; }

    /// <summary>جهات الاتصال — the people Modon and the offices can contact.</summary>
    public List<BuildingContact> Contacts { get; set; } = [];
}

/// <summary>One row of "تفاصيل الفرد": name, e-mail and mobile number.</summary>
public class BuildingContact
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Mobile { get; set; } = "";
}

public enum BuildingStage
{
    Consultant,
    Contractor,
}

/// <summary>How the investor chose the office for a stage.</summary>
public enum SelectionMode
{
    /// <summary>المشروع — offered to every registered office; they submit quotations.</summary>
    OpenTender,

    /// <summary>اختيار مكتب / مقاول محدد — one office is asked and accepts or rejects.</summary>
    Direct,
}

public enum TenderState
{
    /// <summary>Waiting for quotations (open project) or for the invited office's answer.</summary>
    Open,

    /// <summary>A quotation was chosen / the invited office accepted.</summary>
    Awarded,

    /// <summary>All quotations rejected or the project cancelled, or the invited office rejected.</summary>
    Closed,
}

/// <summary>
/// One round of a stage: an open project (with a "Bid-" number, as in the manual) or a direct invitation.
/// A rejected round leaves the request in the selection status; the investor then opens a new round.
/// </summary>
public class BuildingTender
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BuildingStage Stage { get; set; }
    public SelectionMode Mode { get; set; }

    /// <summary>Project number of an open project, e.g. Bid-00080-30-12-25 (Bid-id-dd-MM-yy). Null for direct invitations.</summary>
    public string? Number { get; set; }

    /// <summary>The office asked directly (<see cref="SelectionMode.Direct"/> only).</summary>
    public long? InvitedEstablishmentId { get; set; }

    public TenderState State { get; set; }
    public string OpenedByUserId { get; set; } = "";
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<BuildingQuotation> Quotations { get; set; } = [];
}

public enum QuotationState
{
    /// <summary>Submitted to an open project, waiting for the investor.</summary>
    Submitted,

    /// <summary>Chosen by the investor, or the invited office accepted.</summary>
    Selected,

    /// <summary>Another quotation was chosen, or all were rejected.</summary>
    NotSelected,

    /// <summary>The invited office rejected the request (comment required).</summary>
    Declined,
}

/// <summary>
/// An office's answer in a round: a quotation in an open project, or an invited office's acceptance or rejection.
/// One per office per round.
/// </summary>
public class BuildingQuotation
{
    public long Id { get; set; }
    public long TenderId { get; set; }
    public long RequestId { get; set; }
    public long EstablishmentId { get; set; }
    public Organizations.Establishment? Establishment { get; set; }
    public string SubmittedByUserId { get; set; } = "";
    public QuotationState State { get; set; }

    /// <summary>قيمة عرض السعر in riyals; null when an invited office rejected.</summary>
    public decimal? AmountSar { get; set; }

    /// <summary>تعليق مقدم العرض.</summary>
    public string? Comment { get; set; }

    /// <summary>"للتحقق من تعهدات المكتب الاستشاري" — confirmed by an accepting consulting office.</summary>
    public bool CommitmentsAccepted { get; set; }

    public DateTime SubmittedAt { get; set; }

    // Optional attachment of an accepting office ("مرفق عام خاص بالمكتب الإستشاري / بالمقاول"), in the private store.
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSizeBytes { get; set; }
    public string? AttachmentStorageKey { get; set; }

    /// <summary>The accepting consulting office's phase plan (manual 1.4.4): share of the project and duration.</summary>
    public List<QuotationPhase> Phases { get; set; } = [];
}

/// <summary>The six construction phases (مراحل البناء), in the manual's order.</summary>
public enum ConstructionPhase
{
    /// <summary>تجهيز الموقع</summary>
    SitePreparation = 1,

    /// <summary>اعمال الاساسات</summary>
    Foundations = 2,

    /// <summary>الهياكل الإنشائية</summary>
    StructuralWorks = 3,

    /// <summary>الحوائط والعزل والأرضيات</summary>
    WallsInsulationFloors = 4,

    /// <summary>الأعمال الكهربائية والميكانيكية والمعمارية</summary>
    ElectricalMechanicalArchitectural = 5,

    /// <summary>تشطيب الموقع العام و السور</summary>
    SiteFinishingAndFence = 6,
}

/// <summary>A row of the accepting office's phase table: "نسبة كل مرحلة في المشروع" and "مدة التنفيذ (يوم)".</summary>
public class QuotationPhase
{
    public long QuotationId { get; set; }
    public ConstructionPhase Phase { get; set; }
    public decimal SharePercent { get; set; }
    public int DurationDays { get; set; }
}

/// <summary>
/// A monthly construction-progress report by the selected consulting office (تقارير متابعة البناء الشهرية).
/// One per calendar month per request.
/// </summary>
public class ProgressReport
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public long EstablishmentId { get; set; }
    public Organizations.Establishment? Establishment { get; set; }
    public string SubmittedByUserId { get; set; } = "";
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? Comment { get; set; }

    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSizeBytes { get; set; }
    public string? AttachmentStorageKey { get; set; }

    public List<ProgressReportPhase> Phases { get; set; } = [];
}

/// <summary>Completion percentage of one phase at the end of the reported month.</summary>
public class ProgressReportPhase
{
    public long ReportId { get; set; }
    public ConstructionPhase Phase { get; set; }
    public decimal CompletionPercent { get; set; }
}
