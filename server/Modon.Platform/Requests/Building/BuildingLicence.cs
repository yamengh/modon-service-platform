namespace Modon.Platform.Requests.Building;

/// <summary>Option lists and limits of the building licence, from the building-licence user manual (January 2026).</summary>
public static class BuildingLicence
{
    public sealed record Option(string Code, string NameAr, string NameEn);

    public sealed record PhaseInfo(ConstructionPhase Phase, string NameAr, string NameEn);

    /// <summary>
    /// نوع الرخصة. The manual shows only "ترخيص بناء جديد" (also as the open project's type), so that is the only
    /// option; other licence types are not documented.
    /// </summary>
    public static readonly Option[] LicenceTypes = [new("new-building", "ترخيص بناء جديد", "New building licence")];

    /// <summary>The six phases of the consulting office's phase table, in the manual's order and wording.</summary>
    public static readonly PhaseInfo[] Phases =
    [
        new(ConstructionPhase.SitePreparation, "تجهيز الموقع", "Site preparation"),
        new(ConstructionPhase.Foundations, "أعمال الأساسات", "Foundations"),
        new(ConstructionPhase.StructuralWorks, "الهياكل الإنشائية", "Structural works"),
        new(ConstructionPhase.WallsInsulationFloors, "الحوائط والعزل والأرضيات", "Walls, insulation and floors"),
        new(ConstructionPhase.ElectricalMechanicalArchitectural, "الأعمال الكهربائية والميكانيكية والمعمارية",
            "Electrical, mechanical and architectural works"),
        new(ConstructionPhase.SiteFinishingAndFence, "تشطيب الموقع العام والسور", "Site finishing and fence"),
    ];

    public const int ContractNumberLength = 30;
    public const int ShortText = 200;
    public const int LongText = 2000;
    public const int Phone = 20;
    public const int MaxContacts = 10;
    public const int MaxFloors = 200;
    public const int MaxDurationDays = 3650;

    /// <summary>Riyal amounts are stored as numeric(12,2).</summary>
    public const decimal MaxAmountSar = 9_999_999_999m;

    /// <summary>
    /// The offices' own attachment ("مرفق عام خاص بالمكتب الإستشاري / بالمقاول"): one optional file of up to
    /// 10,240 KB in the formats the manual lists. Also used for progress-report attachments (an assumption: the
    /// manual does not describe report attachments).
    /// </summary>
    public static readonly AttachmentSlot OfficeAttachment = new(
        "officeAttachment", "مرفق عام", "General attachment", false, 10240, ServiceApplications.OfficeDocuments);
}
