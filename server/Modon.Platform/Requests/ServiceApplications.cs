using Modon.Platform.Catalog;

namespace Modon.Platform.Requests;

/// <summary>One upload slot of a service (one file per slot).</summary>
public sealed record AttachmentSlot(
    string Code,
    string LabelAr,
    string LabelEn,
    bool RequiredOnSubmit,
    int MaxSizeKb,
    string[] Extensions);

/// <summary>A catalogue service that currently accepts applications.</summary>
/// <param name="Slots">Attachments of the application itself (editable in a draft or a returned request).</param>
/// <param name="CoordinationSlots">
/// Coordination letters Modon may request after submission (uploadable only while the request is
/// <see cref="RequestStatus.AwaitingCoordinationLetters"/>). Empty for services without that step.
/// </param>
public sealed record ServiceApplication(
    string ServiceCode,
    string NumberPrefix,
    string[] ApplicantRoles,
    AttachmentSlot[] Slots,
    AttachmentSlot[] CoordinationSlots)
{
    public IEnumerable<AttachmentSlot> AllSlots => Slots.Concat(CoordinationSlots);
}

/// <summary>Which services can be applied for, who applies, and their upload slots.</summary>
public static class ServiceApplications
{
    public const int MaxFileSizeKb = 20480;
    private static readonly string[] Images = [".jpeg", ".jpg", ".png"];
    private static readonly string[] ImagesAndPdf = [".jpeg", ".pdf", ".jpg", ".png"];
    private static readonly string[] PdfAndPng = [".pdf", ".png"];

    /// <summary>"JPEG,JPG,PNG,PDF,Doc,Docx,XLS,XLSX" — the building manual's general-attachment formats.</summary>
    public static readonly string[] OfficeDocuments = [".jpeg", ".jpg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"];

    public static readonly ServiceApplication ExcavationPermit = new(
        ServiceCatalogData.ExcavationPermitCode,
        NumberPrefix: "EXC",
        ApplicantRoles: [ModonRoles.Contractor],
        Slots:
        [
            new("workLocationPhoto", "صورة لموقع العمل", "Work location photo", true, MaxFileSizeKb, Images),
            new("workRoutePlan", "مخطط مسار العمل", "Work route plan", true, MaxFileSizeKb, ImagesAndPdf),
            new("excavationCrossSection", "مقطع عرضي للحفر", "Excavation cross-section", true, MaxFileSizeKb, ImagesAndPdf),
            new("projectSchedule", "الجدول الزمني للمشروع", "Project schedule", true, MaxFileSizeKb, ImagesAndPdf),
            new("trafficSafetyPlan", "خطة سلامة السير", "Traffic safety plan", true, MaxFileSizeKb, ImagesAndPdf),
            new("ownerEntityLetter", "خطاب الجهة المالكة", "Owner entity letter", true, MaxFileSizeKb, ImagesAndPdf),
        ],
        // The later-stage coordination letters from the manual (same names as the catalogue's required
        // documents); all three are required to send them.
        CoordinationSlots:
        [
            new("secCoordinationLetter", "خطاب تنسيق الشركة السعودية للكهرباء",
                "Saudi Electricity Company coordination letter", true, MaxFileSizeKb, ImagesAndPdf),
            new("nationalGridCoordinationLetter", "خطاب تنسيق الشركة الوطنية لنقل الكهرباء",
                "National Grid coordination letter", true, MaxFileSizeKb, ImagesAndPdf),
            new("eastElectricityOfficeCoordinationLetter", "خطاب تنسيق مكتب خدمة كهرباء شرق",
                "East electricity service office coordination letter", true, MaxFileSizeKb, ImagesAndPdf),
        ]);

    /// <summary>
    /// Building licence (رخصة البناء), applied for by investors. The manual's upload boxes allow PDF/PNG plans and a
    /// general attachment of up to 10,240 KB. The plans' 1,000,000 KB limit in the manual is capped at the platform's
    /// 20,480 KB, and each slot holds one file (the manual allows up to five) — see MODON.md.
    /// </summary>
    public static readonly ServiceApplication BuildingLicence = new(
        ServiceCatalogData.BuildingLicenceCode,
        NumberPrefix: "BLR",
        ApplicantRoles: [ModonRoles.Investor],
        Slots:
        [
            new("approvedGeneralPlans", "المخططات العامة المعتمدة الصادرة", "Issued approved general plans", true, MaxFileSizeKb, PdfAndPng),
            new("approvedSafetyPlans", "مخططات السلامة المعتمدة الصادرة", "Issued approved safety plans", true, MaxFileSizeKb, PdfAndPng),
            new("generalAttachment", "مرفق عام خاص", "Private general attachment", false, 10240, OfficeDocuments),
        ],
        CoordinationSlots: []);

    private static readonly Dictionary<string, ServiceApplication> ByCode =
        new[] { ExcavationPermit, BuildingLicence }.ToDictionary(x => x.ServiceCode);

    public static ServiceApplication? Find(string serviceCode) => ByCode.GetValueOrDefault(serviceCode);
}
