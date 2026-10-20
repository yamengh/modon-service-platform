using FormCMS.Auth.Models;
using FormCMS.Core.Identities;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Data;
using Modon.Platform.Documents;
using Modon.Platform.Organizations;
using Modon.Platform.Requests.Workflow;

namespace Modon.Platform.Requests.Building;

/// <summary>
/// Building licence use cases (Milestones 6–8). Every method takes the signed-in user and enforces on the server:
/// <list type="bullet">
/// <item>Investor actions: the request's owner, with the investor role, still a member of the request's establishment.</item>
/// <item>Office actions: the consultant or contractor role, AND membership of an establishment registered as a
/// consulting office / contractor (<see cref="Establishment.IsConsultingOffice"/>, <see cref="Establishment.IsContractor"/>).
/// An invited office must be the one the investor chose; reports come only from the selected consulting office.</item>
/// <item>Stage changes go through <see cref="RequestTransitions"/> (rules, workflow engine, exactly one history row,
/// status as concurrency token). Quotations in an open project are data only: they do not change the status.</item>
/// </list>
/// Other users' requests and projects look like 404.
/// </summary>
public sealed class BuildingService(
    ModonDbContext db,
    CmsDbContext identityDb,
    InProcessRequestWorkflow rules,
    BuildingValidator validator,
    UploadValidator uploadValidator,
    IPrivateDocumentStore documents,
    RequestTransitions transitions,
    TimeProvider clock,
    ILogger<BuildingService> logger)
{
    private const string Service = ServiceCatalogData.BuildingLicenceCode;

    /// <summary>The actions and statuses of one stage (the consultant and contractor stages have the same shape).</summary>
    private sealed record Stage(
        BuildingStage Kind, string Role,
        RequestAction OpenTender, RequestAction Select, RequestAction Accept, RequestAction RejectAll,
        RequestAction OfficeAccepted, RequestAction OfficeDeclined,
        RequestStatus Selection, RequestStatus Tender, RequestStatus Awaiting);

    private static readonly Stage ConsultantStage = new(BuildingStage.Consultant, ModonRoles.Consultant,
        RequestAction.OpenConsultantTender, RequestAction.SelectConsultant, RequestAction.AcceptConsultantQuotation,
        RequestAction.RejectConsultantQuotations, RequestAction.ConsultantAccepted, RequestAction.ConsultantDeclined,
        RequestStatus.ConsultantSelection, RequestStatus.ConsultantTender, RequestStatus.AwaitingConsultantDecision);

    private static readonly Stage ContractorStage = new(BuildingStage.Contractor, ModonRoles.Contractor,
        RequestAction.OpenContractorTender, RequestAction.SelectContractor, RequestAction.AcceptContractorQuotation,
        RequestAction.RejectContractorQuotations, RequestAction.ContractorAccepted, RequestAction.ContractorDeclined,
        RequestStatus.ContractorSelection, RequestStatus.ContractorTender, RequestStatus.AwaitingContractorDecision);

    private static Stage StageOf(BuildingStage kind) => kind == BuildingStage.Consultant ? ConsultantStage : ContractorStage;

    /// <summary>The stage whose round is open in this status (tender or invitation), if any.</summary>
    private static Stage? OpenRoundStage(RequestStatus status) => status switch
    {
        RequestStatus.ConsultantTender or RequestStatus.AwaitingConsultantDecision => ConsultantStage,
        RequestStatus.ContractorTender or RequestStatus.AwaitingContractorDecision => ContractorStage,
        _ => null,
    };

    private static readonly RequestAction[] InvestorActions =
    [
        RequestAction.OpenConsultantTender, RequestAction.SelectConsultant,
        RequestAction.AcceptConsultantQuotation, RequestAction.RejectConsultantQuotations,
        RequestAction.OpenContractorTender, RequestAction.SelectContractor,
        RequestAction.AcceptContractorQuotation, RequestAction.RejectContractorQuotations,
    ];

    // =============================================================================================
    // Investor: application form
    // =============================================================================================

    public async Task<IResult?> SaveDraftAsync(UserAccess user, long id, BuildingLicenceDto body, CancellationToken ct)
    {
        var (request, denied) = await LoadInvestorRequestAsync(user, id, ct);
        if (denied is not null) return denied;
        if (request!.Status != RequestStatus.Draft) return Problem(409, ProblemCodes.RequestNotEditable);

        var dto = body.Normalized();
        var errors = await validator.ValidateFormAsync(dto, ValidationMode.Draft, user.Id, [], ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        dto.ApplyTo(request);
        request.UpdatedAt = Now();
        return await SaveOrConflictAsync(ct);
    }

    /// <summary>
    /// Submits the basic information (manual 1.1 "تقديم الطلب"): validates everything, starts the workflow once and
    /// moves the request to consultant selection. Called by <see cref="RequestService.SubmitAsync"/> after its checks.
    /// </summary>
    public async Task<IResult?> SubmitAsync(UserAccess user, ServiceRequest request, CancellationToken ct)
    {
        if (request.Status != RequestStatus.Draft) return Problem(409, ProblemCodes.RequestNotEditable);
        var dto = BuildingLicenceDto.From(request).Normalized();
        var errors = await validator.ValidateFormAsync(dto, ValidationMode.Submit, user.Id,
            request.Attachments.Select(a => a.SlotCode), ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            await transitions.ApplyAndSaveAsync(request, RequestAction.Submit, user.Id, comment: null, ct,
                r => r.SubmittedAt ??= r.UpdatedAt);
        }
        catch (Exception e) when (WorkflowProblems.From(e, ProblemCodes.RequestNotEditable, ProblemCodes.RequestNotEditable) is { } problem)
        {
            return problem;
        }
        logger.LogInformation("Building request {Number} submitted by {User}", request.RequestNumber, user.Id);
        return null;
    }

    // =============================================================================================
    // Investor: consultant and contractor stages
    // =============================================================================================

    /// <summary>"المشروع" (open project) or "اختيار مكتب استشاري محدد / مقاول محدد", then "التقدم في الطلب".</summary>
    public async Task<IResult?> ChooseAsync(UserAccess user, long id, BuildingStage kind, SelectionBody body, CancellationToken ct)
    {
        var (request, denied) = await LoadInvestorRequestAsync(user, id, ct);
        if (denied is not null) return denied;
        var stage = StageOf(kind);

        if (body.Mode is not { } mode || !Enum.IsDefined(mode))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = [ErrorCodes.Required] });
        var action = mode == SelectionMode.OpenTender ? stage.OpenTender : stage.Select;
        if (!rules.Allows(Service, request!.Status, action)) return Problem(409, ProblemCodes.InvalidTransition);

        Establishment? invited = null;
        if (mode == SelectionMode.Direct)
        {
            if (body.EstablishmentId is not { } officeId)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["establishmentId"] = [ErrorCodes.Required] });
            invited = await db.Establishments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == officeId, ct);
            if (invited is null || !BuildingValidator.IsOfficeFor(invited, kind))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["establishmentId"] = [ErrorCodes.NotAnOffice] });
        }

        var round = new BuildingTender
        {
            RequestId = request.Id, Stage = kind, Mode = mode, InvitedEstablishmentId = invited?.Id,
            State = TenderState.Open, OpenedByUserId = user.Id, OpenedAt = Now(),
        };
        var comment = invited is null ? null : $"{invited.NameAr} / {invited.NameEn}";
        if (await TransitionAsync(request, action, user.Id, comment, r => db.BuildingTenders.Add(round), ct) is { } problem)
            return problem;

        if (mode == SelectionMode.OpenTender)
        {
            // The project number shown to the offices, like the manual's "Bid-00080-30-12-25".
            round.Number = $"Bid-{round.Id:D5}-{round.OpenedAt:dd-MM-yy}";
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("Building request {Number}: {Action} by {User}", request.RequestNumber, action, user.Id);
        return null;
    }

    /// <summary>"اختيار عرض سعر": the investor picks one quotation of the open project.</summary>
    public async Task<IResult?> AcceptQuotationAsync(UserAccess user, long id, BuildingStage kind, long quotationId, CancellationToken ct)
    {
        var (request, denied) = await LoadInvestorRequestAsync(user, id, ct);
        if (denied is not null) return denied;
        var stage = StageOf(kind);
        if (!rules.Allows(Service, request!.Status, stage.Accept)) return Problem(409, ProblemCodes.InvalidTransition);

        var round = await OpenRoundAsync(request.Id, kind, ct);
        var chosen = round?.Quotations.FirstOrDefault(q => q.Id == quotationId && q.State == QuotationState.Submitted);
        if (round is null || round.Mode != SelectionMode.OpenTender || chosen is null)
            return Problem(404, ProblemCodes.QuotationNotFound);

        var problem = await TransitionAsync(request, stage.Accept, user.Id, comment: null, r =>
        {
            var now = r.UpdatedAt;
            foreach (var q in round.Quotations.Where(q => q.State == QuotationState.Submitted))
                q.State = q.Id == chosen.Id ? QuotationState.Selected : QuotationState.NotSelected;
            round.State = TenderState.Awarded;
            round.ClosedAt = now;
            SetSelectedOffice(r, kind, chosen.EstablishmentId);
        }, ct);
        if (problem is not null) return problem;

        await CloseLateQuotationsAsync(round.Id, ct);
        return null;
    }

    /// <summary>"رفض جميع العروض / إلغاء المشروع": back to choosing an open project or a specific office.</summary>
    public async Task<IResult?> RejectQuotationsAsync(UserAccess user, long id, BuildingStage kind, InvestorCommentBody body, CancellationToken ct)
    {
        var (request, denied) = await LoadInvestorRequestAsync(user, id, ct);
        if (denied is not null) return denied;
        var stage = StageOf(kind);
        if (!rules.Allows(Service, request!.Status, stage.RejectAll)) return Problem(409, ProblemCodes.InvalidTransition);

        var comment = BuildingLicenceDto.Clean(body.Comment);
        if (comment?.Length > BuildingLicence.LongText)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["comment"] = [ErrorCodes.TooLong] });

        var round = await OpenRoundAsync(request.Id, kind, ct);
        var problem = await TransitionAsync(request, stage.RejectAll, user.Id, comment, r =>
        {
            if (round is null) return;
            foreach (var q in round.Quotations.Where(q => q.State == QuotationState.Submitted)) q.State = QuotationState.NotSelected;
            round.State = TenderState.Closed;
            round.ClosedAt = r.UpdatedAt;
        }, ct);
        if (problem is not null) return problem;

        if (round is not null) await CloseLateQuotationsAsync(round.Id, ct);
        return null;
    }

    /// <summary>Registered consulting offices or contractors, for the investor's direct choice.</summary>
    public async Task<IResult> OfficesAsync(BuildingStage kind, CancellationToken ct)
    {
        var query = db.Establishments.AsNoTracking();
        query = kind == BuildingStage.Consultant ? query.Where(e => e.IsConsultingOffice) : query.Where(e => e.IsContractor);
        return Results.Ok(await query.OrderBy(e => e.NameAr)
            .Select(e => new OfficeInfo(e.Id, e.CrNumber, e.NameAr, e.NameEn)).ToListAsync(ct));
    }

    // =============================================================================================
    // Offices (consulting offices and contractors)
    // =============================================================================================

    /// <summary>The user's open projects (المشاريع المطروحة), invitations, and awarded projects (المشاريع الراسية).</summary>
    public async Task<IResult> ProjectsAsync(UserAccess user, CancellationToken ct)
    {
        var offices = await OfficesOfUserAsync(user, ct);
        if (offices.Consultant.Length == 0 && offices.Contractor.Length == 0) return Results.Ok(Array.Empty<PartnerProjectSummary>());
        var consultantIds = offices.Consultant.Select(o => o.Id).ToArray();
        var contractorIds = offices.Contractor.Select(o => o.Id).ToArray();
        var anyConsultant = consultantIds.Length > 0;
        var anyContractor = contractorIds.Length > 0;

        var openRounds = await db.BuildingTenders.AsNoTracking()
            .Where(t => t.State == TenderState.Open)
            .Where(t => (t.Stage == BuildingStage.Consultant && (t.Mode == SelectionMode.OpenTender ? anyConsultant : consultantIds.Contains(t.InvitedEstablishmentId!.Value)))
                     || (t.Stage == BuildingStage.Contractor && (t.Mode == SelectionMode.OpenTender ? anyContractor : contractorIds.Contains(t.InvitedEstablishmentId!.Value))))
            .Select(t => new { Round = t, Answered = t.Quotations.Any(q => consultantIds.Contains(q.EstablishmentId) || contractorIds.Contains(q.EstablishmentId)) })
            .ToListAsync(ct);
        var awarded = await db.BuildingLicenceDetails.AsNoTracking()
            .Where(d => (d.SelectedConsultantEstablishmentId != null && consultantIds.Contains(d.SelectedConsultantEstablishmentId.Value))
                     || (d.SelectedContractorEstablishmentId != null && contractorIds.Contains(d.SelectedContractorEstablishmentId.Value)))
            .Select(d => new { d.RequestId, Consultant = d.SelectedConsultantEstablishmentId != null && consultantIds.Contains(d.SelectedConsultantEstablishmentId.Value) })
            .ToListAsync(ct);

        var requestIds = openRounds.Select(o => o.Round.RequestId).Concat(awarded.Select(a => a.RequestId)).Distinct().ToArray();
        var requests = await db.ServiceRequests.AsNoTracking()
            .Include(r => r.Building).Include(r => r.Establishment)
            .Where(r => requestIds.Contains(r.Id) && r.ApplicantUserId != user.Id)
            .ToDictionaryAsync(r => r.Id, ct);

        var rows = new List<PartnerProjectSummary>();
        foreach (var o in openRounds)
        {
            if (!requests.TryGetValue(o.Round.RequestId, out var r)) continue;
            rows.Add(Summary(r, o.Round.Number, o.Round.Stage,
                o.Round.Mode == SelectionMode.OpenTender ? PartnerRelation.OpenTender : PartnerRelation.Invited, o.Round.OpenedAt, o.Answered));
        }
        foreach (var a in awarded)
        {
            if (!requests.TryGetValue(a.RequestId, out var r)) continue;
            rows.Add(Summary(r, null, a.Consultant ? BuildingStage.Consultant : BuildingStage.Contractor,
                PartnerRelation.Awarded, r.UpdatedAt, true));
        }
        return Results.Ok(rows.OrderByDescending(x => x.Since).ThenByDescending(x => x.RequestId).ToArray());

        static PartnerProjectSummary Summary(ServiceRequest r, string? number, BuildingStage stage, PartnerRelation relation, DateTime since, bool answered)
            => new(r.Id, r.RequestNumber, number, r.Status, stage, relation, r.Building?.LicenceTypeCode, Office(r.Establishment), since, answered);
    }

    public async Task<IResult> ProjectAsync(UserAccess user, long id, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct);
        return access is null ? Results.NotFound() : Results.Ok(await PartnerDetailAsync(access, ct));
    }

    /// <summary>Manual 1.3 / 1.6: an office quotes for an open project (amount and optional comment).</summary>
    public async Task<IResult> SubmitQuotationAsync(UserAccess user, long id, QuotationBody body, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct, track: true);
        if (access is null) return Results.NotFound();
        if (access.Relation != PartnerRelation.OpenTender || access.Round is not { Mode: SelectionMode.OpenTender } round)
            return Problem(409, ProblemCodes.InvalidTransition);

        var errors = BuildingValidator.ValidateQuotation(body);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var (office, officeError) = await validator.OfficeOfUserAsync(body.EstablishmentId, user.Id, round.Stage, ct);
        if (officeError == ErrorCodes.NotYourEstablishment) return Problem(403, ProblemCodes.NotYourEstablishment);
        if (officeError is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["establishmentId"] = [officeError] });
        if (round.Quotations.Any(q => q.EstablishmentId == office!.Id)) return Problem(409, ProblemCodes.QuotationAlreadySubmitted);

        var now = Now();
        round.Quotations.Add(new BuildingQuotation
        {
            RequestId = access.Request.Id, EstablishmentId = office!.Id, SubmittedByUserId = user.Id,
            State = QuotationState.Submitted, AmountSar = body.AmountSar, Comment = BuildingLicenceDto.Clean(body.Comment),
            SubmittedAt = now,
        });
        // Touching the request makes the save check its status (concurrency token): a quotation is refused if the
        // investor closed the project in the meantime.
        access.Request.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(409, ProblemCodes.RequestChanged);
        }
        catch (DbUpdateException)
        {
            return Problem(409, ProblemCodes.QuotationAlreadySubmitted); // the same office quoted twice at once
        }
        logger.LogInformation("Building request {Number}: {Stage} quotation by office {Office}", access.Request.RequestNumber, round.Stage, office.Id);
        return Results.Ok(await PartnerDetailAsync((await LoadPartnerAccessAsync(user, id, ct))!, ct));
    }

    /// <summary>
    /// Manual 1.4 / 1.7: the office chosen directly accepts (quotation) or rejects (comment). Returns the project
    /// after accepting, and 204 after rejecting (the office then has no access to the project any more).
    /// </summary>
    public async Task<IResult> RespondAsync(UserAccess user, long id, OfficeResponseBody body, IFormFile? file, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct, track: true);
        if (access is null) return Results.NotFound();
        if (access.Relation != PartnerRelation.Invited || access.Round is not { Mode: SelectionMode.Direct } round
            || round.Quotations.Count > 0)
            return Problem(409, ProblemCodes.InvalidTransition);
        var stage = StageOf(round.Stage);

        var errors = BuildingValidator.ValidateResponse(body, round.Stage);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var accept = body.Decision == OfficeDecision.Accept;

        string? storedKey = null;
        BuildingQuotation answer;
        {
            var (stored, uploadError) = accept && file is not null
                ? await StoreOfficeFileAsync(access.Request.Id, "quotations", file, ct)
                : (null, null);
            if (uploadError is not null) return uploadError;
            storedKey = stored?.Key;
            answer = new BuildingQuotation
            {
                RequestId = access.Request.Id, EstablishmentId = round.InvitedEstablishmentId!.Value, SubmittedByUserId = user.Id,
                State = accept ? QuotationState.Selected : QuotationState.Declined,
                AmountSar = accept ? body.AmountSar : null,
                Comment = BuildingLicenceDto.Clean(body.Comment),
                CommitmentsAccepted = accept && body.CommitmentsAccepted,
                SubmittedAt = Now(),
                AttachmentFileName = stored?.FileName, AttachmentContentType = stored?.ContentType,
                AttachmentSizeBytes = stored?.Size, AttachmentStorageKey = stored?.Key,
                Phases = accept && round.Stage == BuildingStage.Consultant
                    ? body.Phases!.OrderBy(p => p.Phase).Select(p => new QuotationPhase
                        { Phase = p.Phase, SharePercent = p.SharePercent!.Value, DurationDays = p.DurationDays!.Value }).ToList()
                    : [],
            };
        }

        var action = accept ? stage.OfficeAccepted : stage.OfficeDeclined;
        IResult? problem;
        try
        {
            problem = await TransitionAsync(access.Request, action, user.Id, answer.Comment, r =>
            {
                round.Quotations.Add(answer);
                round.State = accept ? TenderState.Awarded : TenderState.Closed;
                round.ClosedAt = r.UpdatedAt;
                if (accept) SetSelectedOffice(r, round.Stage, answer.EstablishmentId);
            }, ct);
        }
        catch
        {
            if (storedKey is not null) documents.Delete(storedKey);
            throw;
        }
        if (problem is not null)
        {
            if (storedKey is not null) documents.Delete(storedKey);
            return problem;
        }
        logger.LogInformation("Building request {Number}: {Action} by {User}", access.Request.RequestNumber, action, user.Id);
        // After rejecting, the office has no further access to the project.
        return await LoadPartnerAccessAsync(user, id, ct) is { } after
            ? Results.Ok(await PartnerDetailAsync(after, ct))
            : Results.NoContent();
    }

    /// <summary>
    /// A monthly construction-progress report by the selected consulting office, for an approved licence: the month,
    /// the completion of each of the six phases (0–100 %), a comment and an optional attachment.
    /// </summary>
    public async Task<IResult> SubmitProgressReportAsync(UserAccess user, long id, ProgressReportBody body, IFormFile? file, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct, track: true);
        if (access is null) return Results.NotFound();
        if (access.ReportingOffice is not { } office) return Problem(403, ProblemCodes.RoleNotAllowed);
        if (!rules.Allows(Service, access.Request.Status, RequestAction.SubmitProgressReport))
            return Problem(409, ProblemCodes.InvalidTransition);

        var approvedAt = access.Request.History.Where(h => h.Action == RequestAction.Approve).Select(h => h.OccurredAt).DefaultIfEmpty(access.Request.UpdatedAt).Max();
        var errors = BuildingValidator.ValidateReport(body, Now(), approvedAt, out var period);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.ProgressReports.AnyAsync(p => p.RequestId == id && p.PeriodYear == period.Year && p.PeriodMonth == period.Month, ct))
            return Problem(409, ProblemCodes.ReportPeriodTaken);

        var (stored, uploadError) = file is null ? (null, null) : await StoreOfficeFileAsync(id, "progress-reports", file, ct);
        if (uploadError is not null) return uploadError;

        var label = $"{period.Year:D4}-{period.Month:D2}";
        var report = new ProgressReport
        {
            RequestId = id, EstablishmentId = office.Id, SubmittedByUserId = user.Id,
            PeriodYear = period.Year, PeriodMonth = period.Month, SubmittedAt = Now(),
            Comment = BuildingLicenceDto.Clean(body.Comment),
            AttachmentFileName = stored?.FileName, AttachmentContentType = stored?.ContentType,
            AttachmentSizeBytes = stored?.Size, AttachmentStorageKey = stored?.Key,
            Phases = body.Phases!.OrderBy(p => p.Phase)
                .Select(p => new ProgressReportPhase { Phase = p.Phase, CompletionPercent = p.CompletionPercent!.Value }).ToList(),
        };

        IResult? problem;
        try
        {
            // reportPeriodTaken when the same month was reported at the same moment (unique index).
            problem = await TransitionAsync(access.Request, RequestAction.SubmitProgressReport, user.Id, label,
                r => db.ProgressReports.Add(report), ct, duplicateCode: ProblemCodes.ReportPeriodTaken);
        }
        catch
        {
            if (stored is not null) documents.Delete(stored.Key);
            throw;
        }
        if (problem is not null)
        {
            if (stored is not null) documents.Delete(stored.Key);
            return problem;
        }
        logger.LogInformation("Building request {Number}: progress report {Period} by office {Office}", access.Request.RequestNumber, label, office.Id);
        return Results.Ok(await PartnerDetailAsync((await LoadPartnerAccessAsync(user, id, ct))!, ct));
    }

    /// <summary>Application attachments: only for an office that was invited to or selected for the project.</summary>
    public async Task<IResult> PartnerDownloadAsync(UserAccess user, long id, string slotCode, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct);
        if (access is null || access.Relation == PartnerRelation.OpenTender) return Results.NotFound();
        var attachment = access.Request.Attachments.FirstOrDefault(a => a.SlotCode == slotCode);
        return File(attachment?.StorageKey, attachment?.ContentType, attachment?.FileName);
    }

    /// <summary>An office's own quotation attachment, or the selected office's progress-report attachment.</summary>
    public async Task<IResult> PartnerFileAsync(UserAccess user, long id, string kind, long fileOwnerId, CancellationToken ct)
    {
        var access = await LoadPartnerAccessAsync(user, id, ct);
        if (access is null) return Results.NotFound();
        if (kind == "quotations")
        {
            var mine = access.MyOfficeIds;
            var q = await db.BuildingQuotations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == fileOwnerId && x.RequestId == id && mine.Contains(x.EstablishmentId), ct);
            return File(q?.AttachmentStorageKey, q?.AttachmentContentType, q?.AttachmentFileName);
        }
        if (access.Relation != PartnerRelation.Awarded) return Results.NotFound();
        var report = await db.ProgressReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileOwnerId && x.RequestId == id, ct);
        return File(report?.AttachmentStorageKey, report?.AttachmentContentType, report?.AttachmentFileName);
    }

    /// <summary>Quotation or report attachment of a request the caller may already see (owner or reviewer).</summary>
    public async Task<IResult> RequestFileAsync(long requestId, string kind, long fileOwnerId, CancellationToken ct)
    {
        if (kind == "quotations")
        {
            var q = await db.BuildingQuotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileOwnerId && x.RequestId == requestId, ct);
            return File(q?.AttachmentStorageKey, q?.AttachmentContentType, q?.AttachmentFileName);
        }
        var report = await db.ProgressReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileOwnerId && x.RequestId == requestId, ct);
        return File(report?.AttachmentStorageKey, report?.AttachmentContentType, report?.AttachmentFileName);
    }

    // =============================================================================================
    // Tasks and views
    // =============================================================================================

    /// <summary>
    /// What the workflow waits for from this user: the investor's stage decisions on their own requests, and the
    /// invitations of the user's offices. Open projects are listed separately (<see cref="ProjectsAsync"/>).
    /// </summary>
    public async Task<TaskItem[]> TasksAsync(UserAccess user, (string Ar, string En) serviceName, CancellationToken ct)
    {
        var tasks = new List<TaskItem>();
        if (user.Roles.Contains(ModonRoles.Investor))
        {
            var mine = await db.ServiceRequests.AsNoTracking()
                .Include(r => r.History)
                .Where(r => r.ApplicantUserId == user.Id && r.ServiceCode == Service)
                .Where(r => r.Status == RequestStatus.ConsultantSelection || r.Status == RequestStatus.ContractorSelection
                         || r.Status == RequestStatus.ConsultantTender || r.Status == RequestStatus.ContractorTender)
                .Where(r => r.EstablishmentId == null || db.EstablishmentMembers.Any(m => m.EstablishmentId == r.EstablishmentId && m.UserId == user.Id))
                .AsSplitQuery()
                .ToListAsync(ct);
            var withQuotes = await QuotedOpenRoundsAsync(mine.Select(r => r.Id).ToArray(), ct);
            foreach (var r in mine)
                if (InvestorTask(r, withQuotes.Contains(r.Id)) is { } task)
                    tasks.Add(new TaskItem(r.Id, r.RequestNumber, r.ServiceCode, serviceName.Ar, serviceName.En, r.Status,
                        task.Kind, task.Instructions, task.AssignedAt, r.SubmittedAt));
        }

        var offices = await OfficesOfUserAsync(user, ct);
        var invitedIds = offices.Consultant.Select(o => o.Id).Concat(offices.Contractor.Select(o => o.Id)).ToArray();
        if (invitedIds.Length > 0)
        {
            var invitations = await db.BuildingTenders.AsNoTracking()
                .Where(t => t.State == TenderState.Open && t.Mode == SelectionMode.Direct && invitedIds.Contains(t.InvitedEstablishmentId!.Value))
                .Join(db.ServiceRequests, t => t.RequestId, r => r.Id, (t, r) => new { Round = t, Request = r })
                .Where(x => x.Request.ApplicantUserId != user.Id)
                .ToListAsync(ct);
            foreach (var x in invitations)
            {
                var consultant = x.Round.Stage == BuildingStage.Consultant;
                if (!(consultant ? offices.Consultant : offices.Contractor).Any(o => o.Id == x.Round.InvitedEstablishmentId)) continue;
                tasks.Add(new TaskItem(x.Request.Id, x.Request.RequestNumber, x.Request.ServiceCode, serviceName.Ar, serviceName.En,
                    x.Request.Status, consultant ? ApplicantTaskKind.RespondAsConsultant : ApplicantTaskKind.RespondAsContractor,
                    null, x.Round.OpenedAt, x.Request.SubmittedAt));
            }
        }
        return tasks.ToArray();
    }

    /// <summary>The investor's open task on a building request (shown on the request page).</summary>
    public async Task<ApplicantTask?> CurrentTaskAsync(ServiceRequest r, CancellationToken ct)
        => InvestorTask(r, (await QuotedOpenRoundsAsync([r.Id], ct)).Contains(r.Id));

    private static ApplicantTask? InvestorTask(ServiceRequest r, bool hasQuotations)
    {
        ApplicantTaskKind? kind = r.Status switch
        {
            RequestStatus.ConsultantSelection => ApplicantTaskKind.ChooseConsultant,
            RequestStatus.ContractorSelection => ApplicantTaskKind.ChooseContractor,
            RequestStatus.ConsultantTender when hasQuotations => ApplicantTaskKind.ReviewConsultantQuotations,
            RequestStatus.ContractorTender when hasQuotations => ApplicantTaskKind.ReviewContractorQuotations,
            _ => null,
        };
        if (kind is null) return null;
        // The comment of the step that led here, e.g. why an office rejected the request.
        var entered = r.History.Where(h => h.ToStatus == r.Status).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).LastOrDefault();
        return new ApplicantTask(kind.Value, entered?.Action is RequestAction.ConsultantDeclined or RequestAction.ContractorDeclined
            or RequestAction.RejectConsultantQuotations or RequestAction.RejectContractorQuotations ? entered.Comment : null,
            entered?.OccurredAt ?? r.UpdatedAt);
    }

    /// <summary>The investor's and reviewer's view: form, every round with every quotation, selected offices, reports.</summary>
    public async Task<BuildingView> ViewAsync(ServiceRequest r, bool forInvestor, CancellationToken ct)
    {
        var rounds = await db.BuildingTenders.AsNoTracking()
            .Include(t => t.Quotations).ThenInclude(q => q.Establishment)
            .Where(t => t.RequestId == r.Id)
            .OrderBy(t => t.OpenedAt).ThenBy(t => t.Id)
            .AsSplitQuery()
            .ToListAsync(ct);
        var officeIds = rounds.Where(t => t.InvitedEstablishmentId != null).Select(t => t.InvitedEstablishmentId!.Value)
            .Concat(new[] { r.Building?.SelectedConsultantEstablishmentId, r.Building?.SelectedContractorEstablishmentId }.OfType<long>())
            .Distinct().ToArray();
        var offices = await db.Establishments.AsNoTracking().Where(e => officeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var reports = await ReportsAsync(r.Id, ct);

        var tenderViews = rounds.Select(t => ToView(t, offices)).ToArray();
        QuotationView? Selected(BuildingStage stage) => tenderViews.Where(t => t.Stage == stage && t.State == TenderState.Awarded)
            .SelectMany(t => t.Quotations).LastOrDefault(q => q.State == QuotationState.Selected);

        return new BuildingView(
            BuildingLicenceDto.From(r),
            tenderViews,
            Office(r.Building?.SelectedConsultantEstablishmentId is { } c ? offices.GetValueOrDefault(c) : null),
            Selected(BuildingStage.Consultant),
            Office(r.Building?.SelectedContractorEstablishmentId is { } k ? offices.GetValueOrDefault(k) : null),
            Selected(BuildingStage.Contractor),
            reports,
            CurrentProgress(reports),
            forInvestor ? InvestorActions.Where(a => rules.Allows(Service, r.Status, a)).ToArray() : []);
    }

    // =============================================================================================
    // helpers
    // =============================================================================================

    private sealed record OfficesOfUser(Establishment[] Consultant, Establishment[] Contractor);

    /// <summary>The user's offices that may act in each stage: role AND a registered office of that kind.</summary>
    private async Task<OfficesOfUser> OfficesOfUserAsync(UserAccess user, CancellationToken ct)
    {
        var isConsultant = user.Roles.Contains(ModonRoles.Consultant);
        var isContractor = user.Roles.Contains(ModonRoles.Contractor);
        if (!isConsultant && !isContractor) return new([], []);
        var mine = await db.Establishments.AsNoTracking()
            .Where(e => e.Members.Any(m => m.UserId == user.Id) && (e.IsConsultingOffice || e.IsContractor))
            .OrderBy(e => e.NameAr)
            .ToArrayAsync(ct);
        return new(isConsultant ? mine.Where(e => e.IsConsultingOffice).ToArray() : [],
                   isContractor ? mine.Where(e => e.IsContractor).ToArray() : []);
    }

    private sealed record PartnerAccess(
        ServiceRequest Request,
        PartnerRelation Relation,
        BuildingTender? Round,
        Establishment[] StageOffices,
        long[] MyOfficeIds,
        Establishment? ReportingOffice);

    /// <summary>
    /// How the user's offices relate to the project, or null (→ 404) when they have no business seeing it:
    /// awarded (selected office), invited (the office chosen directly), or open project (an office of the right kind).
    /// </summary>
    private async Task<PartnerAccess?> LoadPartnerAccessAsync(UserAccess user, long id, CancellationToken ct, bool track = false)
    {
        var offices = await OfficesOfUserAsync(user, ct);
        if (offices.Consultant.Length == 0 && offices.Contractor.Length == 0) return null;

        var query = db.ServiceRequests.Include(r => r.Building).Include(r => r.Establishment)
            .Include(r => r.Attachments).Include(r => r.History).AsSplitQuery();
        var request = await (track ? query : query.AsNoTracking())
            .FirstOrDefaultAsync(r => r.Id == id && r.ServiceCode == Service && r.Status != RequestStatus.Draft, ct);
        if (request is null || request.ApplicantUserId == user.Id) return null;

        var myIds = offices.Consultant.Concat(offices.Contractor).Select(o => o.Id).Distinct().ToArray();
        var reportingOffice = offices.Consultant.FirstOrDefault(o => o.Id == request.Building?.SelectedConsultantEstablishmentId);
        var awardedContractor = offices.Contractor.FirstOrDefault(o => o.Id == request.Building?.SelectedContractorEstablishmentId);

        var stage = OpenRoundStage(request.Status);
        var round = stage is null ? null : await OpenRoundAsync(request.Id, stage.Kind, ct, track);
        var stageOffices = stage is null ? [] : stage.Kind == BuildingStage.Consultant ? offices.Consultant : offices.Contractor;

        if (round is { Mode: SelectionMode.Direct } && stageOffices.Any(o => o.Id == round.InvitedEstablishmentId))
            return new PartnerAccess(request, PartnerRelation.Invited, round,
                stageOffices.Where(o => o.Id == round.InvitedEstablishmentId).ToArray(), myIds, reportingOffice);
        if (reportingOffice is not null || awardedContractor is not null)
            return new PartnerAccess(request, PartnerRelation.Awarded, round, [reportingOffice ?? awardedContractor!], myIds, reportingOffice);
        if (round is { Mode: SelectionMode.OpenTender } && stageOffices.Length > 0)
            return new PartnerAccess(request, PartnerRelation.OpenTender, round, stageOffices, myIds, null);
        return null;
    }

    private async Task<PartnerProjectDetail> PartnerDetailAsync(PartnerAccess a, CancellationToken ct)
    {
        var r = a.Request;
        var seesDocuments = a.Relation != PartnerRelation.OpenTender;
        var myQuotations = await db.BuildingQuotations.AsNoTracking()
            .Include(q => q.Establishment)
            .Where(q => q.RequestId == r.Id && a.MyOfficeIds.Contains(q.EstablishmentId))
            .OrderBy(q => q.SubmittedAt)
            .ToListAsync(ct);
        var stageOf = await db.BuildingTenders.AsNoTracking().Where(t => t.RequestId == r.Id)
            .ToDictionaryAsync(t => t.Id, t => t.Stage, ct);

        QuotationView? plan = null;
        ProgressReportView[] reports = [];
        if (a.Relation == PartnerRelation.Awarded)
        {
            plan = await db.BuildingTenders.AsNoTracking()
                .Where(t => t.RequestId == r.Id && t.Stage == BuildingStage.Consultant && t.State == TenderState.Awarded)
                .SelectMany(t => t.Quotations).Include(q => q.Establishment)
                .Where(q => q.State == QuotationState.Selected)
                .OrderByDescending(q => q.SubmittedAt)
                .Select(q => q).FirstOrDefaultAsync(ct) is { } selected ? ToView(selected, BuildingStage.Consultant) : null;
            reports = await ReportsAsync(r.Id, ct);
        }

        var service = ServiceApplications.BuildingLicence;
        var quoted = a.Round?.Quotations.Select(q => q.EstablishmentId).ToHashSet() ?? [];
        var round = a.Round is null ? null : ToView(a.Round, new Dictionary<long, Establishment>(), includeQuotations: false);
        return new PartnerProjectDetail(
            r.Id, r.RequestNumber, r.Status, a.Relation, a.Round?.Stage ?? (a.ReportingOffice is null ? BuildingStage.Contractor : BuildingStage.Consultant),
            round, Office(r.Establishment),
            r.Building?.LicenceTypeCode, r.Building?.ConstructionBudgetSar, r.Building?.FloorCount,
            r.Building?.ScopeOfWork, r.Building?.Notes, r.SubmittedAt,
            a.StageOffices.Select(o => Office(o)!).ToArray(),
            myQuotations.Select(q => ToView(q, stageOf.GetValueOrDefault(q.TenderId))).ToArray(),
            seesDocuments ? service.Slots : [],
            seesDocuments ? r.Attachments.InSlotOrder(service.Slots) : [],
            plan,
            reports,
            CurrentProgress(reports),
            CanQuote: a.Relation == PartnerRelation.OpenTender && a.StageOffices.Any(o => !quoted.Contains(o.Id)),
            CanRespond: a.Relation == PartnerRelation.Invited && a.Round!.Quotations.Count == 0,
            CanReport: a.ReportingOffice is not null && rules.Allows(Service, r.Status, RequestAction.SubmitProgressReport));
    }

    private async Task<ProgressReportView[]> ReportsAsync(long requestId, CancellationToken ct)
    {
        var reports = await db.ProgressReports.AsNoTracking()
            .Include(p => p.Establishment)
            .Where(p => p.RequestId == requestId)
            .OrderBy(p => p.PeriodYear).ThenBy(p => p.PeriodMonth)
            .ToListAsync(ct);
        var authorIds = reports.Select(p => p.SubmittedByUserId).Distinct().ToArray();
        var emails = await identityDb.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        return reports.Select(p => new ProgressReportView(
            p.Id, $"{p.PeriodYear:D4}-{p.PeriodMonth:D2}", Office(p.Establishment)!, p.SubmittedByUserId,
            emails.GetValueOrDefault(p.SubmittedByUserId), p.SubmittedAt, p.Comment,
            p.AttachmentStorageKey is null ? null : new StoredFile(p.AttachmentFileName!, p.AttachmentContentType!, p.AttachmentSizeBytes ?? 0),
            p.Phases.OrderBy(x => x.Phase).Select(x => new PhaseProgressDto(x.Phase, x.CompletionPercent)).ToArray())).ToArray();
    }

    /// <summary>The latest reported completion of each phase (0 until a report mentions it).</summary>
    private static PhaseProgressDto[] CurrentProgress(ProgressReportView[] reports)
        => BuildingLicence.Phases.Select(p => new PhaseProgressDto(p.Phase,
            reports.LastOrDefault()?.Phases.FirstOrDefault(x => x.Phase == p.Phase)?.CompletionPercent ?? 0m)).ToArray();

    private static TenderView ToView(BuildingTender t, IReadOnlyDictionary<long, Establishment> offices, bool includeQuotations = true)
        => new(t.Id, t.Stage, t.Mode, t.Number,
            Office(t.InvitedEstablishmentId is { } i ? offices.GetValueOrDefault(i) : null),
            t.State, t.OpenedAt, t.ClosedAt,
            includeQuotations ? t.Quotations.OrderBy(q => q.SubmittedAt).ThenBy(q => q.Id).Select(q => ToView(q, t.Stage)).ToArray() : []);

    private static QuotationView ToView(BuildingQuotation q, BuildingStage stage) => new(
        q.Id, q.TenderId, stage, Office(q.Establishment) ?? new OfficeInfo(q.EstablishmentId, "", "", ""),
        q.State, q.AmountSar, q.Comment, q.CommitmentsAccepted, q.SubmittedAt,
        q.AttachmentStorageKey is null ? null : new StoredFile(q.AttachmentFileName!, q.AttachmentContentType!, q.AttachmentSizeBytes ?? 0),
        q.Phases.OrderBy(p => p.Phase).Select(p => new PhasePlanDto(p.Phase, p.SharePercent, p.DurationDays)).ToArray());

    private static OfficeInfo? Office(Establishment? e) => e is null ? null : new OfficeInfo(e.Id, e.CrNumber, e.NameAr, e.NameEn);

    private static void SetSelectedOffice(ServiceRequest r, BuildingStage stage, long establishmentId)
    {
        var d = r.Building ??= new BuildingLicenceDetails();
        if (stage == BuildingStage.Consultant) d.SelectedConsultantEstablishmentId = establishmentId;
        else d.SelectedContractorEstablishmentId = establishmentId;
    }

    /// <summary>The open round of a stage (there is at most one: a new round can only start from the selection status).</summary>
    private async Task<BuildingTender?> OpenRoundAsync(long requestId, BuildingStage stage, CancellationToken ct, bool track = true)
    {
        var query = db.BuildingTenders.Include(t => t.Quotations)
            .Where(t => t.RequestId == requestId && t.Stage == stage && t.State == TenderState.Open)
            .OrderByDescending(t => t.OpenedAt).ThenByDescending(t => t.Id);
        return await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<HashSet<long>> QuotedOpenRoundsAsync(long[] requestIds, CancellationToken ct)
        => (await db.BuildingTenders.AsNoTracking()
                .Where(t => requestIds.Contains(t.RequestId) && t.State == TenderState.Open && t.Mode == SelectionMode.OpenTender
                            && t.Quotations.Any(q => q.State == QuotationState.Submitted))
                .Select(t => t.RequestId).ToListAsync(ct))
            .ToHashSet();

    /// <summary>
    /// A quotation saved in the instant between loading the round and closing it stays "Submitted"; mark it as not
    /// selected so a closed round never has pending quotations.
    /// </summary>
    private Task CloseLateQuotationsAsync(long roundId, CancellationToken ct)
        => db.BuildingQuotations.Where(q => q.TenderId == roundId && q.State == QuotationState.Submitted)
            .ExecuteUpdateAsync(u => u.SetProperty(q => q.State, QuotationState.NotSelected), ct);

    /// <param name="duplicateCode">Problem title when a unique index refuses the save (the same answer or month twice).</param>
    private async Task<IResult?> TransitionAsync(ServiceRequest request, RequestAction action, string actorUserId, string? comment,
        Action<ServiceRequest> sideEffects, CancellationToken ct, string duplicateCode = ProblemCodes.RequestChanged)
    {
        try
        {
            await transitions.ApplyAndSaveAsync(request, action, actorUserId, comment, ct, sideEffects);
            return null;
        }
        // invalidTransition: not possible now (or the workflow already moved on); requestChanged: someone else acted first.
        catch (Exception e) when (WorkflowProblems.From(e, ProblemCodes.InvalidTransition, ProblemCodes.RequestChanged) is { } problem)
        {
            return problem;
        }
        // A parallel request saved the same row first (e.g. two answers of one office); nothing of this one was saved.
        catch (DbUpdateException)
        {
            return Problem(409, duplicateCode);
        }
    }

    /// <summary>The owner (investor role), still a member of the request's establishment once submitted.</summary>
    private async Task<(ServiceRequest? Request, IResult? Denied)> LoadInvestorRequestAsync(UserAccess user, long id, CancellationToken ct)
    {
        var request = await db.ServiceRequests
            .Include(r => r.Building).ThenInclude(b => b!.Contacts)
            .Include(r => r.Attachments)
            .Include(r => r.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id && r.ApplicantUserId == user.Id, ct);
        if (request is null) return (null, Results.NotFound());
        if (request.ServiceCode != Service) return (null, Problem(400, ProblemCodes.WrongService));
        if (!user.Roles.Contains(ModonRoles.Investor)) return (null, Problem(403, ProblemCodes.RoleNotAllowed));
        if (request.SubmittedAt is not null && request.EstablishmentId is { } establishmentId
            && !await db.EstablishmentMembers.AnyAsync(m => m.EstablishmentId == establishmentId && m.UserId == user.Id, ct))
            return (null, Problem(403, ProblemCodes.NotYourEstablishment));
        return (request, null);
    }

    private sealed record StoredUpload(string Key, string FileName, string ContentType, long Size);

    private async Task<(StoredUpload? Stored, IResult? Error)> StoreOfficeFileAsync(long requestId, string folder, IFormFile file, CancellationToken ct)
    {
        var slot = BuildingLicence.OfficeAttachment;
        var (accepted, error) = await uploadValidator.ValidateAsync(file, slot, ct);
        if (error is not null) return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [error] }));
        // The storage key never contains the user's file name.
        var key = $"requests/{requestId}/{folder}/{Guid.NewGuid():N}{accepted!.Extension}";
        await using (var stream = file.OpenReadStream())
            await documents.SaveAsync(key, stream, ct);
        return (new StoredUpload(key, RequestService.SafeFileName(file.FileName, accepted.Extension), accepted.ContentType, file.Length), null);
    }

    private IResult File(string? storageKey, string? contentType, string? fileName)
    {
        var stream = storageKey is null ? null : documents.OpenRead(storageKey);
        return stream is null ? Results.NotFound() : Results.File(stream, contentType, fileName);
    }

    private async Task<IResult?> SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(409, ProblemCodes.RequestChanged);
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static IResult Problem(int status, string code) => Results.Problem(title: code, statusCode: status);
}
