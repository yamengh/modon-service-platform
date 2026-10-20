using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;

namespace Modon.Platform.Requests.Building;

/// <summary>
/// Server-side rules of the building licence. Returns field → error codes (empty when valid); the portal translates
/// the codes. Field names match the JSON bodies, e.g. "contacts[0].email" or "phases[2].sharePercent".
/// </summary>
public sealed partial class BuildingValidator(ModonDbContext db)
{
    /// <summary>A contract or registration number: digits, Latin letters, "-" and "/" (e.g. 25000168).</summary>
    [GeneratedRegex(@"^[0-9A-Za-z/-]+$")]
    private static partial Regex ContractNumberPattern();

    [GeneratedRegex(@"^(\d{4})-(\d{2})$")]
    private static partial Regex PeriodPattern();

    /// <summary>The application form. Draft: formats only. Submit: also every required field, the terms and the plans.</summary>
    public async Task<Dictionary<string, string[]>> ValidateFormAsync(
        BuildingLicenceDto dto, ValidationMode mode, string userId, IEnumerable<string> uploadedSlots, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string code) => errors.TryAdd(field, [code]);
        var submit = mode == ValidationMode.Submit;

        // السجل التجاري: one of the user's own establishments (organisation-based authorization).
        if (dto.EstablishmentId is { } establishmentId)
        {
            if (!await db.EstablishmentMembers.AnyAsync(m => m.EstablishmentId == establishmentId && m.UserId == userId, ct))
                Add("establishmentId", ErrorCodes.NotYourEstablishment);
        }
        else if (submit) Add("establishmentId", ErrorCodes.Required);

        if (dto.ContractNumber is { } contract)
        {
            if (contract.Length > BuildingLicence.ContractNumberLength) Add("contractNumber", ErrorCodes.TooLong);
            else if (!ContractNumberPattern().IsMatch(contract)) Add("contractNumber", ErrorCodes.InvalidFormat);
        }
        else if (submit) Add("contractNumber", ErrorCodes.Required);

        if (dto.LicenceTypeCode is { } type)
        {
            if (BuildingLicence.LicenceTypes.All(t => t.Code != type)) Add("licenceTypeCode", ErrorCodes.UnknownOption);
        }
        else if (submit) Add("licenceTypeCode", ErrorCodes.Required);

        Amount("constructionBudgetSar", dto.ConstructionBudgetSar, submit, Add);

        if (dto.FloorCount is { } floors)
        {
            if (floors < 1) Add("floorCount", ErrorCodes.MustBePositive);
            else if (floors > BuildingLicence.MaxFloors) Add("floorCount", ErrorCodes.TooLarge);
        }
        else if (submit) Add("floorCount", ErrorCodes.Required);

        // إطار العمل and ملاحظات إضافية are not marked required in the manual.
        if (dto.ScopeOfWork?.Length > BuildingLicence.LongText) Add("scopeOfWork", ErrorCodes.TooLong);
        if (dto.Notes?.Length > BuildingLicence.LongText) Add("notes", ErrorCodes.TooLong);

        // جهات الاتصال: optional, but every listed person needs a name, e-mail and mobile (the "تفاصيل الفرد" table).
        var contacts = dto.Contacts ?? [];
        if (contacts.Length > BuildingLicence.MaxContacts) Add("contacts", ErrorCodes.TooMany);
        for (var i = 0; i < contacts.Length && i < BuildingLicence.MaxContacts; i++)
        {
            var c = contacts[i];
            var prefix = $"contacts[{i}]";
            if (c.Name is null) Add($"{prefix}.name", ErrorCodes.Required);
            else if (c.Name.Length > BuildingLicence.ShortText) Add($"{prefix}.name", ErrorCodes.TooLong);
            if (c.Email is null) Add($"{prefix}.email", ErrorCodes.Required);
            else if (c.Email.Length > BuildingLicence.ShortText) Add($"{prefix}.email", ErrorCodes.TooLong);
            else if (!ExcavationValidator.IsEmail(c.Email)) Add($"{prefix}.email", ErrorCodes.InvalidEmail);
            if (c.Mobile is null) Add($"{prefix}.mobile", ErrorCodes.Required);
            else if (c.Mobile.Length > BuildingLicence.Phone) Add($"{prefix}.mobile", ErrorCodes.TooLong);
            else if (!ExcavationValidator.IsPhone(c.Mobile)) Add($"{prefix}.mobile", ErrorCodes.InvalidPhone);
        }

        if (submit)
        {
            if (!dto.TermsAccepted) Add("termsAccepted", ErrorCodes.Required);
            var uploaded = uploadedSlots.ToHashSet();
            foreach (var slot in ServiceApplications.BuildingLicence.Slots.Where(s => s.RequiredOnSubmit))
                if (!uploaded.Contains(slot.Code)) Add($"attachments.{slot.Code}", ErrorCodes.Required);
        }
        return errors;
    }

    /// <summary>
    /// The office the user acts for: required, one the user belongs to (403 otherwise, checked by the caller via
    /// <see cref="ErrorCodes.NotYourEstablishment"/>), and registered as the right kind of office for the stage.
    /// </summary>
    public async Task<(Organizations.Establishment? Office, string? Error)> OfficeOfUserAsync(
        long? establishmentId, string userId, BuildingStage stage, CancellationToken ct)
    {
        if (establishmentId is not { } id) return (null, ErrorCodes.Required);
        var office = await db.Establishments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.Members.Any(m => m.UserId == userId), ct);
        if (office is null) return (null, ErrorCodes.NotYourEstablishment);
        return IsOfficeFor(office, stage) ? (office, null) : (null, ErrorCodes.NotAnOffice);
    }

    public static bool IsOfficeFor(Organizations.Establishment e, BuildingStage stage)
        => stage == BuildingStage.Consultant ? e.IsConsultingOffice : e.IsContractor;

    /// <summary>A quotation in an open project: amount required, comment optional.</summary>
    public static Dictionary<string, string[]> ValidateQuotation(QuotationBody body)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string code) => errors.TryAdd(field, [code]);
        Amount("amountSar", body.AmountSar, required: true, Add);
        if (BuildingLicenceDto.Clean(body.Comment)?.Length > BuildingLicence.LongText) Add("comment", ErrorCodes.TooLong);
        return errors;
    }

    /// <summary>An invited office's answer (manual 1.4.4 and 1.7.4).</summary>
    public static Dictionary<string, string[]> ValidateResponse(OfficeResponseBody body, BuildingStage stage)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string code) => errors.TryAdd(field, [code]);
        var comment = BuildingLicenceDto.Clean(body.Comment);
        if (comment?.Length > BuildingLicence.LongText) Add("comment", ErrorCodes.TooLong);

        switch (body.Decision)
        {
            case null:
                Add("decision", ErrorCodes.Required);
                break;
            case OfficeDecision.Reject:
                // "في حالة رفض … يقوم المستخدم بإدخال التعليق"
                if (comment is null) Add("comment", ErrorCodes.Required);
                break;
            case OfficeDecision.Accept:
                Amount("amountSar", body.AmountSar, required: true, Add);
                if (stage == BuildingStage.Consultant)
                {
                    // "يقوم المستخدم بإدخال نسب المراحل والمدة المطلوبة وإدخال عرض السعر والموافقة على التحقق من تعهدات المكتب"
                    if (!body.CommitmentsAccepted) Add("commitmentsAccepted", ErrorCodes.Required);
                    ValidatePhases(body.Phases?.Select(p => (p.Phase, (decimal?)p.SharePercent, (int?)p.DurationDays, true)).ToArray(),
                        "phases", "sharePercent", Add);
                }
                break;
        }
        return errors;
    }

    /// <summary>A monthly report: a month that has started and is not before the approval month, and all six phases 0–100.</summary>
    public static Dictionary<string, string[]> ValidateReport(ProgressReportBody body, DateTime nowUtc, DateTime approvedAtUtc,
        out (int Year, int Month) period)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string code) => errors.TryAdd(field, [code]);
        period = default;

        var match = PeriodPattern().Match(body.Period?.Trim() ?? "");
        if (body.Period is null || body.Period.Trim().Length == 0) Add("period", ErrorCodes.Required);
        else if (!match.Success || int.Parse(match.Groups[2].Value) is < 1 or > 12 || int.Parse(match.Groups[1].Value) < 2000)
            Add("period", ErrorCodes.InvalidFormat);
        else
        {
            period = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            var month = period.Year * 12 + period.Month;
            if (month > nowUtc.Year * 12 + nowUtc.Month) Add("period", ErrorCodes.FuturePeriod);
            else if (month < approvedAtUtc.Year * 12 + approvedAtUtc.Month) Add("period", ErrorCodes.PeriodBeforeApproval);
        }

        ValidatePhases(body.Phases?.Select(p => (p.Phase, p.CompletionPercent, (int?)null, false)).ToArray(),
            "phases", "completionPercent", Add);
        if (BuildingLicenceDto.Clean(body.Comment)?.Length > BuildingLicence.LongText) Add("comment", ErrorCodes.TooLong);
        return errors;
    }

    /// <summary>
    /// Exactly the six phases, each once, every percentage 0–100 (at most two decimals). The percentages are NOT
    /// required to add up to 100: the manual does not state that rule (its own example adds up to 116).
    /// </summary>
    private static void ValidatePhases(
        (ConstructionPhase Phase, decimal? Percent, int? Days, bool NeedsDays)[]? phases,
        string field, string percentField, Action<string, string> add)
    {
        if (phases is null || phases.Length == 0)
        {
            add(field, ErrorCodes.Required);
            return;
        }
        var seen = new HashSet<ConstructionPhase>();
        for (var i = 0; i < phases.Length; i++)
        {
            var (phase, percent, days, needsDays) = phases[i];
            var prefix = $"{field}[{i}]";
            if (!Enum.IsDefined(phase)) add($"{prefix}.phase", ErrorCodes.UnknownOption);
            else if (!seen.Add(phase)) add($"{prefix}.phase", ErrorCodes.DuplicatePhase);

            if (percent is null) add($"{prefix}.{percentField}", ErrorCodes.Required);
            else if (percent < 0 || percent > 100) add($"{prefix}.{percentField}", ErrorCodes.OutOfRange);
            else if (decimal.Round(percent.Value, 2) != percent) add($"{prefix}.{percentField}", ErrorCodes.InvalidFormat);

            if (!needsDays) continue;
            if (days is null) add($"{prefix}.durationDays", ErrorCodes.Required);
            else if (days < 1) add($"{prefix}.durationDays", ErrorCodes.MustBePositive);
            else if (days > BuildingLicence.MaxDurationDays) add($"{prefix}.durationDays", ErrorCodes.TooLarge);
        }
        if (seen.Count != BuildingLicence.Phases.Length && phases.Length < BuildingLicence.Phases.Length)
            add(field, ErrorCodes.Required); // a phase is missing
        else if (phases.Length > BuildingLicence.Phases.Length) add(field, ErrorCodes.TooMany);
    }

    private static void Amount(string field, decimal? value, bool required, Action<string, string> add)
    {
        if (value is null)
        {
            if (required) add(field, ErrorCodes.Required);
            return;
        }
        if (value <= 0) add(field, ErrorCodes.MustBePositive);
        else if (value > BuildingLicence.MaxAmountSar) add(field, ErrorCodes.TooLarge);
        else if (decimal.Round(value.Value, 2) != value) add(field, ErrorCodes.InvalidFormat);
    }
}
