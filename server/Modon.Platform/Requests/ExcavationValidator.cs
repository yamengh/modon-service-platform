using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;
using Modon.Platform.Lookups;
using C = Modon.Platform.Lookups.LookupSchema.Categories;

namespace Modon.Platform.Requests;

/// <summary>Error codes returned in ValidationProblem "errors"; the portal translates them.</summary>
public static class ErrorCodes
{
    public const string Required = "required";
    public const string TooLong = "tooLong";
    public const string InvalidEmail = "invalidEmail";
    public const string InvalidPhone = "invalidPhone";
    public const string UnknownOption = "unknownOption";
    public const string NotYourEstablishment = "notYourEstablishment";
    public const string CannotChange = "cannotChange";
    public const string MustBePositive = "mustBePositive";
    public const string TooLarge = "tooLarge";
    public const string EndDateMustBeAfterIssueDate = "endDateMustBeAfterIssueDate";
    public const string FileEmpty = "fileEmpty";
    public const string FileTooLarge = "fileTooLarge";
    public const string FileTypeNotAllowed = "fileTypeNotAllowed";
    public const string FileContentMismatch = "fileContentMismatch";
}

public enum ValidationMode
{
    /// <summary>Format checks only: drafts may be incomplete.</summary>
    Draft,

    /// <summary>Format checks plus every required field and attachment.</summary>
    Submit,
}

public sealed partial class ExcavationValidator(ModonDbContext db, ILookupService lookups)
{
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9][0-9 ]{6,18}$")]
    private static partial Regex PhonePattern();

    /// <summary>Returns field → error codes; empty when valid. Expects a normalized DTO.</summary>
    public async Task<Dictionary<string, string[]>> ValidateAsync(
        ExcavationDto dto, ValidationMode mode, string userId, IEnumerable<string> uploadedSlots, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string code) => errors.TryAdd(field, [code]);
        var submit = mode == ValidationMode.Submit;

        // Establishment: must be one the user belongs to (organisation-based authorization).
        if (dto.EstablishmentId is { } establishmentId)
        {
            var isMember = await db.EstablishmentMembers
                .AnyAsync(m => m.EstablishmentId == establishmentId && m.UserId == userId, ct);
            if (!isMember) Add("establishmentId", ErrorCodes.NotYourEstablishment);
        }
        else if (submit) Add("establishmentId", ErrorCodes.Required);

        await Option("requestTypeCode", dto.RequestTypeCode, C.ExcavationRequestType);
        await Option("contractTypeCode", dto.ContractTypeCode, C.ExcavationContractType);
        await Option("industrialCityCode", dto.IndustrialCityCode, C.IndustrialCity);
        await Option("permitTypeCode", dto.PermitTypeCode, C.ExcavationPermitType);
        await Option("excavationMethodCode", dto.ExcavationMethodCode, C.ExcavationMethod);

        Text("workDescription", dto.WorkDescription, ExcavationLimits.WorkDescription);
        Text("workLocation", dto.WorkLocation, ExcavationLimits.ShortText);
        Text("projectManagerName", dto.ProjectManagerName, ExcavationLimits.ShortText);
        Text("supervisingEntity", dto.SupervisingEntity, ExcavationLimits.ShortText);
        if (Text("projectManagerPhone", dto.ProjectManagerPhone, ExcavationLimits.Phone)
            && !PhonePattern().IsMatch(dto.ProjectManagerPhone!))
            Add("projectManagerPhone", ErrorCodes.InvalidPhone);
        if (Text("projectManagerEmail", dto.ProjectManagerEmail, ExcavationLimits.ShortText)
            && !EmailPattern().IsMatch(dto.ProjectManagerEmail!))
            Add("projectManagerEmail", ErrorCodes.InvalidEmail);

        if (dto.IssueDate is null && submit) Add("issueDate", ErrorCodes.Required);
        if (dto.EndDate is null && submit) Add("endDate", ErrorCodes.Required);
        if (dto.IssueDate is { } issue && dto.EndDate is { } end && end <= issue)
            Add("endDate", ErrorCodes.EndDateMustBeAfterIssueDate);

        // Dimensions are optional (the manual does not mark them required), but must be sensible if given.
        Dimension("lengthM", dto.LengthM);
        Dimension("widthM", dto.WidthM);
        Dimension("depthM", dto.DepthM);
        Dimension("totalPathLengthM", dto.TotalPathLengthM);

        if (submit)
        {
            var uploaded = uploadedSlots.ToHashSet();
            foreach (var slot in ServiceApplications.ExcavationPermit.Slots.Where(s => s.RequiredOnSubmit))
            {
                if (!uploaded.Contains(slot.Code)) Add($"attachments.{slot.Code}", ErrorCodes.Required);
            }
        }

        return errors;

        async Task Option(string field, string? code, string category)
        {
            if (code is null)
            {
                if (submit) Add(field, ErrorCodes.Required);
                return;
            }
            if (!(await lookups.CodesAsync(category, ct)).Contains(code)) Add(field, ErrorCodes.UnknownOption);
        }

        // Returns true when a value is present and within the length limit (so format checks can follow).
        bool Text(string field, string? value, int maxLength)
        {
            if (value is null)
            {
                if (submit) Add(field, ErrorCodes.Required);
                return false;
            }
            if (value.Length <= maxLength) return true;
            Add(field, ErrorCodes.TooLong);
            return false;
        }

        void Dimension(string field, decimal? value)
        {
            if (value is null) return;
            if (value <= 0) Add(field, ErrorCodes.MustBePositive);
            else if (value > ExcavationLimits.MaxDimensionM) Add(field, ErrorCodes.TooLarge);
        }
    }
}
