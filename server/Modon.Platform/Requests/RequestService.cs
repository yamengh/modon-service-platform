using FormCMS.Core.Identities;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Data;
using Modon.Platform.Documents;
using Modon.Platform.Requests.Workflow;

namespace Modon.Platform.Requests;

/// <summary>
/// Request use cases. Every method takes the signed-in user and enforces:
/// ownership (other users' requests look like 404), the service's applicant roles (403),
/// membership of the request's establishment once it was submitted (403), and the workflow's
/// editable states (409). Status changes go only through <see cref="RequestTransitions"/>,
/// which always writes exactly one history row.
/// </summary>
public sealed class RequestService(
    ModonDbContext db,
    IRequestWorkflow workflow,
    ExcavationValidator validator,
    UploadValidator uploadValidator,
    IPrivateDocumentStore documents,
    ServiceCatalogNames serviceNames,
    RequestTransitions transitions,
    TimeProvider clock,
    ILogger<RequestService> logger)
{
    public async Task<IResult> CreateAsync(UserAccess user, CreateRequestBody body, CancellationToken ct)
    {
        var service = ServiceApplications.Find(body.ServiceCode ?? "");
        if (service is null) return Problem(400, ProblemCodes.ServiceNotOpen);
        if (!HasApplicantRole(user, service)) return Problem(403, ProblemCodes.RoleNotAllowed);

        var now = Now();
        var request = new ServiceRequest
        {
            ServiceCode = service.ServiceCode,
            RequestNumber = $"TMP-{Guid.NewGuid():N}",
            Status = workflow.InitialStatus,
            ApplicantUserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
            Excavation = new ExcavationPermitDetails(),
        };
        request.History.Add(new RequestStatusHistory
        {
            FromStatus = null, ToStatus = request.Status, Action = RequestAction.Create,
            ActorUserId = user.Id, OccurredAt = now,
        });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync(ct);
        request.RequestNumber = $"{service.NumberPrefix}-{request.Id:D6}-{request.CreatedAt:dd-MM-yy}";
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Created($"/api/modon/requests/{request.Id}", await ToDetailAsync(request, ct));
    }

    public async Task<IResult> ListMineAsync(UserAccess user, CancellationToken ct)
    {
        var requests = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.ApplicantUserId == user.Id)
            .ToListAsync(ct);
        var names = await serviceNames.AllAsync(ct);
        return Results.Ok(requests
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r =>
            {
                var (ar, en) = names.GetValueOrDefault(r.ServiceCode, (r.ServiceCode, r.ServiceCode));
                return new RequestSummary(r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status,
                    r.CreatedAt, r.UpdatedAt, r.SubmittedAt);
            }));
    }

    /// <summary>
    /// The applicant's task inbox: their own requests that Modon returned or asked letters for, limited to
    /// services they may act on and establishments they still belong to (the same rules the actions enforce).
    /// </summary>
    public async Task<IResult> ListTasksAsync(UserAccess user, CancellationToken ct)
    {
        var taskStatuses = Enum.GetValues<RequestStatus>().Where(workflow.IsApplicantTask).ToArray();
        var requests = await db.ServiceRequests.AsNoTracking()
            .Include(r => r.History)
            .Where(r => r.ApplicantUserId == user.Id && taskStatuses.Contains(r.Status))
            .Where(r => r.EstablishmentId == null || db.EstablishmentMembers
                .Any(m => m.EstablishmentId == r.EstablishmentId && m.UserId == user.Id))
            .AsSplitQuery()
            .ToListAsync(ct);
        var names = await serviceNames.AllAsync(ct);

        return Results.Ok(requests
            .Where(r => ServiceApplications.Find(r.ServiceCode) is { } service && HasApplicantRole(user, service))
            .Select(r =>
            {
                var task = r.CurrentTask(workflow)!;
                var (ar, en) = names.GetValueOrDefault(r.ServiceCode, (r.ServiceCode, r.ServiceCode));
                return new TaskItem(r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status,
                    task.Kind, task.Instructions, task.AssignedAt, r.SubmittedAt);
            })
            .OrderByDescending(t => t.AssignedAt).ThenByDescending(t => t.RequestId)
            .ToArray());
    }

    public async Task<IResult> GetAsync(UserAccess user, long id, CancellationToken ct)
    {
        var request = await LoadOwnedAsync(user, id, ct);
        return request is null ? Results.NotFound() : Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> SaveExcavationAsync(UserAccess user, long id, ExcavationDto body, CancellationToken ct)
    {
        var (request, _, denied) = await LoadActionableAsync(user, id, ct);
        if (denied is not null) return denied;
        if (!workflow.IsEditableByApplicant(request!.Status)) return Problem(409, ProblemCodes.RequestNotEditable);

        var dto = body.Normalized();
        var errors = await validator.ValidateAsync(dto, ValidationMode.Draft, user.Id, [], ct);
        AddLockedFieldErrors(request, dto, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        dto.ApplyTo(request);
        request.UpdatedAt = Now();
        return await SaveOrConflictAsync(ct) ?? Results.Ok(await ToDetailAsync(request, ct));
    }

    /// <summary>Submits a draft, or resubmits a returned request (same request, same number).</summary>
    public async Task<IResult> SubmitAsync(UserAccess user, long id, CancellationToken ct)
    {
        var (request, _, denied) = await LoadActionableAsync(user, id, ct);
        if (denied is not null) return denied;
        if (!workflow.IsEditableByApplicant(request!.Status) || workflow.ApplicantSubmitAction(request.Status) is not { } action)
            return Problem(409, ProblemCodes.RequestNotEditable);

        var dto = ExcavationDto.From(request).Normalized();
        var errors = await validator.ValidateAsync(dto, ValidationMode.Submit, user.Id,
            request.Attachments.Select(a => a.SlotCode), ct);
        AddLockedFieldErrors(request, dto, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            // The first submission starts the Camunda process; a resubmission continues the same instance.
            await transitions.ApplyAndSaveAsync(request, action, user.Id, comment: null, ct,
                r => r.SubmittedAt ??= r.UpdatedAt); // the first submission date is kept on resubmission
        }
        catch (Exception e) when (WorkflowProblems.From(e, ProblemCodes.RequestNotEditable, ProblemCodes.RequestNotEditable) is { } problem)
        {
            return problem;
        }

        logger.LogInformation("Request {Number}: {Action} by {User}", request.RequestNumber, action, user.Id);
        return Results.Ok(await ToDetailAsync(request, ct));
    }

    /// <summary>Sends the requested coordination letters; all of them must be uploaded.</summary>
    public async Task<IResult> SubmitCoordinationLettersAsync(UserAccess user, long id, CancellationToken ct)
    {
        var (request, service, denied) = await LoadActionableAsync(user, id, ct);
        if (denied is not null) return denied;
        if (!workflow.AcceptsCoordinationLetters(request!.Status)) return Problem(409, ProblemCodes.RequestNotEditable);

        var uploaded = request.Attachments.Select(a => a.SlotCode).ToHashSet();
        var errors = service!.CoordinationSlots
            .Where(s => s.RequiredOnSubmit && !uploaded.Contains(s.Code))
            .ToDictionary(s => $"attachments.{s.Code}", _ => new[] { ErrorCodes.Required });
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            await transitions.ApplyAndSaveAsync(request, RequestAction.SubmitCoordinationLetters, user.Id, comment: null, ct);
        }
        catch (Exception e) when (WorkflowProblems.From(e, ProblemCodes.RequestNotEditable, ProblemCodes.RequestNotEditable) is { } problem)
        {
            return problem;
        }

        logger.LogInformation("Request {Number}: coordination letters sent by {User}", request.RequestNumber, user.Id);
        return Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> UploadAsync(UserAccess user, long id, string slotCode, IFormFile? file, CancellationToken ct)
    {
        var (request, service, denied) = await LoadActionableAsync(user, id, ct);
        if (denied is not null) return denied;

        var (slot, slotDenied) = ChangeableSlot(request!, service!, slotCode);
        if (slotDenied is not null) return slotDenied;

        var field = $"attachments.{slot!.Code}";
        if (file is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [ErrorCodes.Required] });

        var (accepted, error) = await uploadValidator.ValidateAsync(file, slot, ct);
        if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });

        // The storage key never contains the user's file name.
        var key = $"requests/{request!.Id}/{slot.Code}/{Guid.NewGuid():N}{accepted!.Extension}";
        await using (var stream = file.OpenReadStream())
        {
            await documents.SaveAsync(key, stream, ct);
        }

        var attachment = request.Attachments.FirstOrDefault(a => a.SlotCode == slot.Code);
        var replacedKey = attachment?.StorageKey;
        if (attachment is null)
        {
            attachment = new RequestAttachment { SlotCode = slot.Code };
            request.Attachments.Add(attachment);
        }
        attachment.FileName = SafeFileName(file.FileName, accepted.Extension);
        attachment.ContentType = accepted.ContentType;
        attachment.SizeBytes = file.Length;
        attachment.StorageKey = key;
        attachment.UploadedByUserId = user.Id;
        attachment.UploadedAt = Now();
        request.UpdatedAt = attachment.UploadedAt;

        IResult? conflict;
        try
        {
            conflict = await SaveOrConflictAsync(ct);
        }
        catch
        {
            documents.Delete(key);
            throw;
        }
        if (conflict is not null)
        {
            documents.Delete(key);
            return conflict;
        }
        if (replacedKey is not null) documents.Delete(replacedKey);

        return Results.Ok(attachment.ToInfo());
    }

    public async Task<IResult> DeleteAttachmentAsync(UserAccess user, long id, string slotCode, CancellationToken ct)
    {
        var (request, service, denied) = await LoadActionableAsync(user, id, ct);
        if (denied is not null) return denied;

        var (_, slotDenied) = ChangeableSlot(request!, service!, slotCode);
        if (slotDenied is not null) return slotDenied;

        var attachment = request!.Attachments.FirstOrDefault(a => a.SlotCode == slotCode);
        if (attachment is null) return Results.NotFound();

        db.RequestAttachments.Remove(attachment);
        request.UpdatedAt = Now();
        if (await SaveOrConflictAsync(ct) is { } conflict) return conflict;
        documents.Delete(attachment.StorageKey);
        return Results.NoContent();
    }

    public async Task<IResult> DownloadAsync(UserAccess user, long id, string slotCode, CancellationToken ct)
    {
        var request = await LoadOwnedAsync(user, id, ct);
        var attachment = request?.Attachments.FirstOrDefault(a => a.SlotCode == slotCode);
        var stream = attachment is null ? null : documents.OpenRead(attachment.StorageKey);
        if (stream is null) return Results.NotFound();

        return Results.File(stream, attachment!.ContentType, attachment.FileName);
    }

    public async Task<IResult> MyEstablishmentsAsync(UserAccess user, CancellationToken ct)
        => Results.Ok(await db.EstablishmentMembers.AsNoTracking()
            .Where(m => m.UserId == user.Id)
            .OrderBy(m => m.Establishment!.NameAr)
            .Select(m => new EstablishmentInfo(m.EstablishmentId, m.Establishment!.CrNumber,
                m.Establishment.NameAr, m.Establishment.NameEn))
            .ToListAsync(ct));

    // --- helpers -------------------------------------------------------------------------------

    private Task<ServiceRequest?> LoadOwnedAsync(UserAccess user, long id, CancellationToken ct)
        => db.ServiceRequests
            .Include(r => r.Excavation)
            .Include(r => r.Attachments)
            .Include(r => r.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id && r.ApplicantUserId == user.Id, ct);

    /// <summary>
    /// Loads a request the user owns and may act on. Once a request has been submitted it belongs to its
    /// establishment, so the user must still be a member of it (organisation-based authorization).
    /// Each caller then checks that its action is allowed in the request's status.
    /// </summary>
    private async Task<(ServiceRequest? Request, ServiceApplication? Service, IResult? Denied)> LoadActionableAsync(
        UserAccess user, long id, CancellationToken ct)
    {
        var request = await LoadOwnedAsync(user, id, ct);
        if (request is null) return (null, null, Results.NotFound());

        var service = ServiceApplications.Find(request.ServiceCode);
        if (service is null) return (null, null, Problem(409, ProblemCodes.RequestNotEditable));
        if (!HasApplicantRole(user, service)) return (null, null, Problem(403, ProblemCodes.RoleNotAllowed));

        if (request.SubmittedAt is not null && request.EstablishmentId is { } establishmentId
            && !await db.EstablishmentMembers.AnyAsync(m => m.EstablishmentId == establishmentId && m.UserId == user.Id, ct))
            return (null, null, Problem(403, ProblemCodes.NotYourEstablishment));

        return (request, service, null);
    }

    /// <summary>
    /// The slot being uploaded to or cleared, if it may change now: application attachments while the
    /// form is editable, coordination letters while Modon is waiting for them.
    /// </summary>
    private (AttachmentSlot? Slot, IResult? Denied) ChangeableSlot(ServiceRequest request, ServiceApplication service, string slotCode)
    {
        if (service.Slots.FirstOrDefault(s => s.Code == slotCode) is { } applicationSlot)
            return workflow.IsEditableByApplicant(request.Status)
                ? (applicationSlot, null)
                : (null, Problem(409, ProblemCodes.RequestNotEditable));
        if (service.CoordinationSlots.FirstOrDefault(s => s.Code == slotCode) is { } letterSlot)
            return workflow.AcceptsCoordinationLetters(request.Status)
                ? (letterSlot, null)
                : (null, Problem(409, ProblemCodes.RequestNotEditable));
        return (null, Problem(404, ProblemCodes.UnknownSlot));
    }

    /// <summary>
    /// After the first submission the request belongs to its establishment, so a returned request cannot be
    /// moved to another establishment while it is being completed.
    /// </summary>
    private static void AddLockedFieldErrors(ServiceRequest request, ExcavationDto dto, Dictionary<string, string[]> errors)
    {
        if (request.SubmittedAt is not null && dto.EstablishmentId != request.EstablishmentId)
            errors.TryAdd("establishmentId", [ErrorCodes.CannotChange]);
    }

    /// <summary>
    /// Saves; returns 409 when someone else changed the request's status in the meantime
    /// (<see cref="ServiceRequest.Status"/> is a concurrency token).
    /// </summary>
    private async Task<IResult?> SaveOrConflictAsync(CancellationToken ct, string code = ProblemCodes.RequestChanged)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(409, code);
        }
    }

    private async Task<RequestDetail> ToDetailAsync(ServiceRequest r, CancellationToken ct)
    {
        var (ar, en) = await serviceNames.NameOfAsync(r.ServiceCode, ct);
        var service = ServiceApplications.Find(r.ServiceCode);
        var slots = service?.Slots ?? [];
        var letterSlots = service?.CoordinationSlots ?? [];
        return new RequestDetail(
            r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status,
            workflow.IsEditableByApplicant(r.Status),
            r.CreatedAt, r.UpdatedAt, r.SubmittedAt,
            ExcavationDto.From(r),
            slots,
            r.Attachments.InSlotOrder([.. slots, .. letterSlots]),
            r.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => new HistoryItem(h.FromStatus, h.ToStatus, h.Action, h.OccurredAt, h.Comment)).ToArray(),
            letterSlots,
            workflow.AcceptsCoordinationLetters(r.Status) && letterSlots.Length > 0,
            r.CurrentTask(workflow));
    }

    private static bool HasApplicantRole(UserAccess user, ServiceApplication service)
        => service.ApplicantRoles.Any(user.Roles.Contains);

    private static string SafeFileName(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? ""));
        name = new string(name.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        if (name.Length == 0) name = "document";
        if (name.Length > 200) name = name[..200];
        return name + extension;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static IResult Problem(int status, string code) => Results.Problem(title: code, statusCode: status);
}
