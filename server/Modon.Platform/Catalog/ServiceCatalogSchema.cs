using FormCMS.Core.Descriptors;
using FormCMS.Utils.DisplayModels;
using Attribute = FormCMS.Core.Descriptors.Attribute;

namespace Modon.Platform.Catalog;

/// <summary>
/// The service catalogue is a regular FormCMS entity, so it can be edited from the FormCMS admin panel,
/// and it is read through FormCMS saved queries (public, output-cached).
/// </summary>
public static class ServiceCatalogSchema
{
    public const string EntityName = "modonService";
    public const string ListQueryName = "modonServices";
    public const string DetailQueryName = "modonService";

    public static class Beneficiary
    {
        public const string Investor = "investor";
        public const string Contractor = "contractor";
    }

    // String columns appear in list queries; Text columns are returned by the detail query only.
    private static Attribute Str(string field, string header, string validation = "required")
        => new(field, header, DataType.String, DisplayType.Text, Validation: validation);

    private static Attribute Txt(string field, string header)
        => new(field, header, DataType.Text, DisplayType.Textarea, InList: false);

    public static readonly Entity Entity = new(
        Attributes:
        [
            Str("code", "Code", "^[a-z0-9-]+$"),
            Str("nameAr", "Name (Arabic)"),
            Str("nameEn", "Name (English)"),
            Str("summaryAr", "Summary (Arabic)"),
            Str("summaryEn", "Summary (English)"),
            new Attribute("beneficiary", "Beneficiary", DataType.String, DisplayType.Dropdown,
                Options: $"{Beneficiary.Investor},{Beneficiary.Contractor}", Validation: "required"),
            Str("durationAr", "Duration (Arabic)", ""),
            Str("durationEn", "Duration (English)", ""),
            Str("channelAr", "Channel (Arabic)", ""),
            Str("channelEn", "Channel (English)", ""),
            Str("costAr", "Cost (Arabic)", ""),
            Str("costEn", "Cost (English)", ""),
            new Attribute("sortOrder", "Sort order", DataType.Int, DisplayType.Number),
            Txt("descriptionAr", "Description (Arabic)"),
            Txt("descriptionEn", "Description (English)"),
            Txt("stepsAr", "Steps (Arabic, one per line)"),
            Txt("stepsEn", "Steps (English, one per line)"),
            Txt("requiredDocumentsAr", "Required documents (Arabic, one per line)"),
            Txt("requiredDocumentsEn", "Required documents (English, one per line)"),
        ],
        Name: EntityName,
        DisplayName: "Modon Service",
        TableName: "modon_services",
        LabelAttributeName: "nameEn",
        PrimaryKey: "id",
        DefaultPublicationStatus: PublicationStatus.Published);

    private const string ListFields =
        "id code nameAr nameEn summaryAr summaryEn beneficiary durationAr durationEn sortOrder";

    private const string DetailFields =
        ListFields + " channelAr channelEn costAr costEn descriptionAr descriptionEn " +
        "stepsAr stepsEn requiredDocumentsAr requiredDocumentsEn";

    /// <summary>GET /api/queries/modonServices</summary>
    // FormCMS needs an explicit Asc/Desc suffix on sort fields (a bare name is truncated).
    public static readonly string ListQuerySource = $$"""
        query {{ListQueryName}} {
          {{EntityName}}List(sort: sortOrderAsc) { {{ListFields}} }
        }
        """;

    /// <summary>GET /api/queries/modonService/single?code=excavation-permit</summary>
    public static readonly string DetailQuerySource = $$"""
        query {{DetailQueryName}}($code: String) {
          {{EntityName}}(codeSet: [$code]) { {{DetailFields}} }
        }
        """;
}
