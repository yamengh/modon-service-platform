namespace Modon.Platform.Requests;

public static class ExcavationLimits
{
    public const int ShortText = 200;
    public const int Phone = 20;
    public const int WorkDescription = 2000;
    public const decimal MaxDimensionM = 100_000m;
}

/// <summary>Excavation form data as sent and received by the portal. Every field may be empty in a draft.</summary>
public sealed record ExcavationDto(
    long? EstablishmentId = null,
    string? RequestTypeCode = null,
    string? ContractTypeCode = null,
    string? IndustrialCityCode = null,
    string? PermitTypeCode = null,
    string? WorkDescription = null,
    DateOnly? IssueDate = null,
    DateOnly? EndDate = null,
    string? ExcavationMethodCode = null,
    string? WorkLocation = null,
    decimal? LengthM = null,
    decimal? WidthM = null,
    decimal? DepthM = null,
    decimal? TotalPathLengthM = null,
    string? ProjectManagerName = null,
    string? ProjectManagerPhone = null,
    string? ProjectManagerEmail = null,
    string? SupervisingEntity = null)
{
    public static ExcavationDto From(ServiceRequest request)
    {
        var d = request.Excavation ?? new ExcavationPermitDetails();
        return new ExcavationDto(
            request.EstablishmentId, d.RequestTypeCode, d.ContractTypeCode, d.IndustrialCityCode, d.PermitTypeCode,
            d.WorkDescription, d.IssueDate, d.EndDate, d.ExcavationMethodCode, d.WorkLocation,
            d.LengthM, d.WidthM, d.DepthM, d.TotalPathLengthM,
            d.ProjectManagerName, d.ProjectManagerPhone, d.ProjectManagerEmail, d.SupervisingEntity);
    }

    /// <summary>Trims text and turns blank strings into null, so "  " never counts as filled in.</summary>
    public ExcavationDto Normalized() => this with
    {
        RequestTypeCode = Clean(RequestTypeCode),
        ContractTypeCode = Clean(ContractTypeCode),
        IndustrialCityCode = Clean(IndustrialCityCode),
        PermitTypeCode = Clean(PermitTypeCode),
        WorkDescription = Clean(WorkDescription),
        ExcavationMethodCode = Clean(ExcavationMethodCode),
        WorkLocation = Clean(WorkLocation),
        ProjectManagerName = Clean(ProjectManagerName),
        ProjectManagerPhone = Clean(ProjectManagerPhone),
        ProjectManagerEmail = Clean(ProjectManagerEmail),
        SupervisingEntity = Clean(SupervisingEntity),
    };

    public void ApplyTo(ServiceRequest request)
    {
        request.EstablishmentId = EstablishmentId;
        var d = request.Excavation ??= new ExcavationPermitDetails();
        d.RequestTypeCode = RequestTypeCode;
        d.ContractTypeCode = ContractTypeCode;
        d.IndustrialCityCode = IndustrialCityCode;
        d.PermitTypeCode = PermitTypeCode;
        d.WorkDescription = WorkDescription;
        d.IssueDate = IssueDate;
        d.EndDate = EndDate;
        d.ExcavationMethodCode = ExcavationMethodCode;
        d.WorkLocation = WorkLocation;
        d.LengthM = LengthM;
        d.WidthM = WidthM;
        d.DepthM = DepthM;
        d.TotalPathLengthM = TotalPathLengthM;
        d.ProjectManagerName = ProjectManagerName;
        d.ProjectManagerPhone = ProjectManagerPhone;
        d.ProjectManagerEmail = ProjectManagerEmail;
        d.SupervisingEntity = SupervisingEntity;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
