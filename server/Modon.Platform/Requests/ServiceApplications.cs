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
public sealed record ServiceApplication(
    string ServiceCode,
    string NumberPrefix,
    string[] ApplicantRoles,
    AttachmentSlot[] Slots);

/// <summary>
/// Which services can be applied for in this milestone. The building licence is in the catalogue
/// but not here yet, so the API refuses to create requests for it.
/// </summary>
public static class ServiceApplications
{
    public const int MaxFileSizeKb = 20480;
    private static readonly string[] Images = [".jpeg", ".jpg", ".png"];
    private static readonly string[] ImagesAndPdf = [".jpeg", ".pdf", ".jpg", ".png"];

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
        ]);

    private static readonly Dictionary<string, ServiceApplication> ByCode =
        new[] { ExcavationPermit }.ToDictionary(x => x.ServiceCode);

    public static ServiceApplication? Find(string serviceCode) => ByCode.GetValueOrDefault(serviceCode);
}
