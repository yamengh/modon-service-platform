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
/// and the workflow's editable states (409). Status changes go only through <see cref="RequestTransitions"/>,
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

    public async Task<IResult> GetAsync(UserAccess user, long id, CancellationToken ct)
    {
        var request = await LoadOwnedAsync(user, id, ct);
        return request is null ? Results.NotFound() : Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> SaveExcavationAsync(UserAccess user, long id, ExcavationDto body, CancellationToken ct)
    {
        var (request, service, denied) = await LoadForChangeAsync(user, id, ct);
        if (denied is not null) return denied;

        var dto = body.Normalized();
        var errors = await validator.ValidateAsync(dto, ValidationMode.Draft, user.Id, [], ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        dto.ApplyTo(request!);
        request!.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> SubmitAsync(UserAccess user, long id, CancellationToken ct)
    {
        var (request, _, denied) = await LoadForChangeAsync(user, id, ct);
        if (denied is not null) return denied;

        var dto = ExcavationDto.From(request!).Normalized();
        var errors = await validator.ValidateAsync(dto, ValidationMode.Submit, user.Id,
            request!.Attachments.Select(a => a.SlotCode), ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await transitions.ApplyAsync(request, RequestAction.Submit, user.Id, comment: null, ct);
        request.SubmittedAt = request.UpdatedAt;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(409, ProblemCodes.RequestNotEditable); // submitted twice at the same moment
        }
        logger.LogInformation("Request {Number} submitted by {User}", request.RequestNumber, user.Id);
        return Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> UploadAsync(UserAccess user, long id, string slotCode, IFormFile? file, CancellationToken ct)
    {
        var (request, service, denied) = await LoadForChangeAsync(user, id, ct);
        if (denied is not null) return denied;

        var slot = service!.Slots.FirstOrDefault(s => s.Code == slotCode);
        if (slot is null) return Problem(404, ProblemCodes.UnknownSlot);

        var field = $"attachments.{slot.Code}";
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            documents.Delete(key);
            throw;
        }
        if (replacedKey is not null) documents.Delete(replacedKey);

        return Results.Ok(attachment.ToInfo());
    }

    public async Task<IResult> DeleteAttachmentAsync(UserAccess user, long id, string slotCode, CancellationToken ct)
    {
        var (request, _, denied) = await LoadForChangeAsync(user, id, ct);
        if (denied is not null) return denied;

        var attachment = request!.Attachments.FirstOrDefault(a => a.SlotCode == slotCode);
        if (attachment is null) return Results.NotFound();

        db.RequestAttachments.Remove(attachment);
        request.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
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

    /// <summary>Loads a request the user owns, may act on, and that is still editable.</summary>
    private async Task<(ServiceRequest? Request, ServiceApplication? Service, IResult? Denied)> LoadForChangeAsync(
        UserAccess user, long id, CancellationToken ct)
    {
        var request = await LoadOwnedAsync(user, id, ct);
        if (request is null) return (null, null, Results.NotFound());

        var service = ServiceApplications.Find(request.ServiceCode);
        if (service is null) return (null, null, Problem(409, ProblemCodes.RequestNotEditable));
        if (!HasApplicantRole(user, service)) return (null, null, Problem(403, ProblemCodes.RoleNotAllowed));
        if (!workflow.IsEditableByApplicant(request.Status)) return (null, null, Problem(409, ProblemCodes.RequestNotEditable));

        return (request, service, null);
    }

    private async Task<RequestDetail> ToDetailAsync(ServiceRequest r, CancellationToken ct)
    {
        var (ar, en) = await serviceNames.NameOfAsync(r.ServiceCode, ct);
        var slots = ServiceApplications.Find(r.ServiceCode)?.Slots ?? [];
        return new RequestDetail(
            r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status,
            workflow.IsEditableByApplicant(r.Status),
            r.CreatedAt, r.UpdatedAt, r.SubmittedAt,
            ExcavationDto.From(r),
            slots,
            r.Attachments.InSlotOrder(slots),
            r.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => new HistoryItem(h.FromStatus, h.ToStatus, h.Action, h.OccurredAt, h.Comment)).ToArray());
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
