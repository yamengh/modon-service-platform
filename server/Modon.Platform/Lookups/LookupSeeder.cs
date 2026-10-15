using Modon.Platform.Seeding;
using C = Modon.Platform.Lookups.LookupSchema.Categories;

namespace Modon.Platform.Lookups;

/// <summary>
/// Seeds the dropdown options. The excavation manual shows these fields as dropdowns but does not list
/// their values, so everything except the industrial cities is a PLACEHOLDER awaiting confirmation from
/// Modon. Admins can edit, add or unpublish options in FormCMS; the seeder never overwrites them.
/// </summary>
public static class LookupSeeder
{
    private static Dictionary<string, object> Option(string category, string code, string ar, string en, int order)
        => new() { ["category"] = category, ["code"] = code, ["nameAr"] = ar, ["nameEn"] = en, ["sortOrder"] = order };

    public static readonly Dictionary<string, object>[] Options =
    [
        Option(C.IndustrialCity, "riyadh-2", "المدينة الصناعية الثانية بالرياض", "Riyadh 2nd Industrial City", 1),
        Option(C.IndustrialCity, "riyadh-3", "المدينة الصناعية الثالثة بالرياض", "Riyadh 3rd Industrial City", 2),
        Option(C.IndustrialCity, "dammam-2", "المدينة الصناعية الثانية بالدمام", "Dammam 2nd Industrial City", 3),
        Option(C.IndustrialCity, "jeddah-1", "المدينة الصناعية الأولى بجدة", "Jeddah 1st Industrial City", 4),
        Option(C.IndustrialCity, "al-kharj", "المدينة الصناعية بالخرج", "Al-Kharj Industrial City", 5),

        Option(C.ExcavationRequestType, "new", "طلب جديد", "New request", 1),
        Option(C.ExcavationRequestType, "extension", "تمديد تصريح", "Permit extension", 2),

        Option(C.ExcavationContractType, "bill-of-quantities", "مقايسة", "Bill of quantities", 1),
        Option(C.ExcavationContractType, "investor-contract", "عقد مع مستثمر", "Contract with an investor", 2),

        Option(C.ExcavationPermitType, "electricity", "تمديدات كهربائية", "Electrical works", 1),
        Option(C.ExcavationPermitType, "water", "تمديدات مياه", "Water works", 2),
        Option(C.ExcavationPermitType, "sewage", "صرف صحي", "Sewage works", 3),
        Option(C.ExcavationPermitType, "telecom", "اتصالات", "Telecommunications", 4),

        Option(C.ExcavationMethod, "open-trench", "حفر مكشوف", "Open trench", 1),
        Option(C.ExcavationMethod, "horizontal-drilling", "حفر أفقي موجه", "Horizontal directional drilling", 2),
        Option(C.ExcavationMethod, "manual", "حفر يدوي", "Manual excavation", 3),
    ];

    public static Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
        => FormCmsSeeding.RunAsSystemAsync(services, async sp =>
        {
            await FormCmsSeeding.EnsureEntityAsync(sp, LookupSchema.Entity, ct);
            await FormCmsSeeding.EnsureQueryAsync(sp, LookupSchema.QueryName, LookupSchema.EntityName,
                LookupSchema.QuerySource, ct);
            await FormCmsSeeding.EnsureRecordsAsync(sp, LookupSchema.EntityName, ["category", "code"], Options, ct);
        });
}
