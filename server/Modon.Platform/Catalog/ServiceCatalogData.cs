namespace Modon.Platform.Catalog;

/// <summary>
/// Initial catalogue entries, taken from the Modon portal user manuals (January 2026).
/// After the first run, admins maintain these records in FormCMS; the seeder never overwrites them.
/// </summary>
public static class ServiceCatalogData
{
    public const string BuildingLicenceCode = "building-licence";
    public const string ExcavationPermitCode = "excavation-permit";

    public static readonly Dictionary<string, object>[] Services =
    [
        new()
        {
            ["code"] = BuildingLicenceCode,
            ["nameAr"] = "رخصة البناء وتقارير البناء",
            ["nameEn"] = "Building Licence & Construction Reports",
            ["summaryAr"] = "تتيح للمستثمر الحصول على رخصة بناء للبدء في أعمال إنشاء المصنع، ومتابعة نسبة الإنجاز شهرياً.",
            ["summaryEn"] = "Lets an investor obtain a building licence to start constructing a factory, then track monthly construction progress.",
            ["beneficiary"] = ServiceCatalogSchema.Beneficiary.Investor,
            ["durationAr"] = "",
            ["durationEn"] = "",
            ["channelAr"] = "تطبيق الويب",
            ["channelEn"] = "Web application",
            ["costAr"] = "",
            ["costEn"] = "",
            ["sortOrder"] = 1,
            ["descriptionAr"] =
                "يقدم المستثمر طلب رخصة البناء، ثم يختار مكتباً استشارياً (بطرح مشروع لعروض الأسعار أو بتحديد مكتب مباشرة)، " +
                "ثم يختار المقاول. بعد ذلك يراجع مسؤول المدينة الصناعية الطلب. يحدّث المكتب الاستشاري نسب إنجاز المراحل شهرياً وفق تقارير متابعة البناء.",
            ["descriptionEn"] =
                "The investor submits a building-licence request, selects a consulting office (open tender or direct selection), " +
                "then selects a contractor. The industrial-city officer then reviews the request. The consulting office updates phase completion percentages monthly from construction follow-up reports.",
            ["stepsAr"] = string.Join('\n',
                "تقديم معلومات الطلب الأساسية",
                "اختيار المكتب الاستشاري (مشروع أو مكتب محدد)",
                "استلام عروض أسعار المكاتب الاستشارية واختيار العرض",
                "اختيار المقاول واستلام عروض الأسعار",
                "مراجعة الطلب من مسؤول المدينة الصناعية",
                "المتابعة الشهرية لنسبة البناء وتقارير البناء"),
            ["stepsEn"] = string.Join('\n',
                "Submit the basic request information",
                "Select a consulting office (open project or specific office)",
                "Receive consultant quotations and choose one",
                "Select a contractor and receive quotations",
                "Review by the industrial-city officer",
                "Monthly construction-percentage monitoring and building reports"),
            ["requiredDocumentsAr"] = string.Join('\n',
                "المخططات العامة المعتمدة الصادرة",
                "مخططات السلامة المعتمدة الصادرة",
                "مرفق عام (اختياري)"),
            ["requiredDocumentsEn"] = string.Join('\n',
                "Issued approved general plans",
                "Issued approved safety plans",
                "General attachment (optional)"),
        },
        new()
        {
            ["code"] = ExcavationPermitCode,
            ["nameAr"] = "تصريح أعمال الحفر",
            ["nameEn"] = "Excavation Works Permit",
            ["summaryAr"] = "تتيح للمقاول طلب تصريح أعمال حفر لتنفيذ المشاريع.",
            ["summaryEn"] = "Lets a contractor request an excavation works permit to carry out projects.",
            ["beneficiary"] = ServiceCatalogSchema.Beneficiary.Contractor,
            ["durationAr"] = "3 أيام عمل",
            ["durationEn"] = "3 working days",
            ["channelAr"] = "تطبيق الويب",
            ["channelEn"] = "Web application",
            ["costAr"] = "تكلفة الخدمة توضح حسب المتطلبات",
            ["costEn"] = "Cost depends on the requirements",
            ["sortOrder"] = 2,
            ["descriptionAr"] =
                "يقدم المقاول طلب تصريح أعمال الحفر مع تفاصيل العمل والأبعاد ومدير المشروع والمرفقات. " +
                "تراجع مدن الطلب، وقد تعيده لإكمال النواقص أو تطلب إرفاق الخطابات التنسيقية.",
            ["descriptionEn"] =
                "The contractor submits an excavation permit request with work details, dimensions, project manager and attachments. " +
                "Modon reviews it and may return it for completion or request coordination letters.",
            ["stepsAr"] = string.Join('\n',
                "الدخول على بوابة مدن",
                "اختيار خدمات مدن",
                "اختيار تصريح أعمال الحفر"),
            ["stepsEn"] = string.Join('\n',
                "Sign in to the Modon portal",
                "Open Modon services",
                "Choose Excavation Works Permit"),
            ["requiredDocumentsAr"] = string.Join('\n',
                "صورة لموقع العمل",
                "مخطط مسار العمل",
                "مقطع عرضي للحفر",
                "الجدول الزمني للمشروع",
                "خطة سلامة السير",
                "خطاب الجهة المالكة",
                "الخطابات التنسيقية عند طلبها (الشركة السعودية للكهرباء، الشركة الوطنية لنقل الكهرباء، مكتب خدمة كهرباء شرق)"),
            ["requiredDocumentsEn"] = string.Join('\n',
                "Work location photo",
                "Work route plan",
                "Excavation cross-section",
                "Project schedule",
                "Traffic safety plan",
                "Owner entity letter",
                "Coordination letters when requested (Saudi Electricity Co., National Grid, East Electricity Service Office)"),
        },
    ];
}
