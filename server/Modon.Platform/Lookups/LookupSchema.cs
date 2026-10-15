using FormCMS.Core.Descriptors;
using FormCMS.Utils.DisplayModels;
using Attribute = FormCMS.Core.Descriptors.Attribute;

namespace Modon.Platform.Lookups;

/// <summary>
/// Dropdown options (industrial cities, permit types, …) are one FormCMS entity, so Modon admins can
/// maintain them in FormCMS. Only published records are offered and accepted.
/// Read with: GET /api/queries/modonLookups?category=industrialCity
/// </summary>
public static class LookupSchema
{
    public const string EntityName = "modonLookup";
    public const string QueryName = "modonLookups";

    public static class Categories
    {
        public const string IndustrialCity = "industrialCity";
        public const string ExcavationRequestType = "excavationRequestType";
        public const string ExcavationContractType = "excavationContractType";
        public const string ExcavationPermitType = "excavationPermitType";
        public const string ExcavationMethod = "excavationMethod";

        public static readonly string[] All =
            [IndustrialCity, ExcavationRequestType, ExcavationContractType, ExcavationPermitType, ExcavationMethod];
    }

    public static readonly Entity Entity = new(
        Attributes:
        [
            new Attribute("category", "Category", DataType.String, DisplayType.Dropdown,
                Options: string.Join(",", Categories.All), Validation: "required"),
            new Attribute("code", "Code", DataType.String, DisplayType.Text, Validation: "^[a-z0-9-]+$"),
            new Attribute("nameAr", "Name (Arabic)", DataType.String, DisplayType.Text, Validation: "required"),
            new Attribute("nameEn", "Name (English)", DataType.String, DisplayType.Text, Validation: "required"),
            new Attribute("sortOrder", "Sort order", DataType.Int, DisplayType.Number),
        ],
        Name: EntityName,
        DisplayName: "Modon Lookup",
        TableName: "modon_lookups",
        LabelAttributeName: "nameEn",
        PrimaryKey: "id",
        DefaultPublicationStatus: PublicationStatus.Published);

    // FormCMS needs an explicit Asc/Desc suffix on sort fields (a bare name is truncated)
    // and the primary key in every list selection.
    public static readonly string QuerySource = $$"""
        query {{QueryName}}($category: String) {
          {{EntityName}}List(categorySet: [$category], sort: [sortOrderAsc, idAsc]) { id code nameAr nameEn }
        }
        """;
}
