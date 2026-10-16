using System.Text.Json;
using FormCMS.Auth.Models;
using FormCMS.Core.Identities;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Data;
using Modon.Platform.Documents;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Workflow;

namespace Modon.Platform.Review;

/// <summary>
/// Reviewer use cases. The endpoints only call this after checking the reviewer role.
/// Reviewers see every request that is not a draft; decisions go through <see cref="RequestTransitions"/>
/// (legal transitions only, exactly one history row each).
/// </summary>
public sealed class ReviewService(
    ModonDbContext db,
    CmsDbContext identityDb,
    IRequestWorkflow workflow,
    RequestTransitions transitions,
    ServiceCatalogNames serviceNames,
    IPrivateDocumentStore documents,
    ILogger<ReviewService> logger)
{
    public const int MaxCommentLength = 2000;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private IQueryable<ServiceRequest> Visible()
        => db.ServiceRequests.Where(r => r.Status != RequestStatus.Draft);

    public async Task<IResult> SummaryAsync(CancellationToken ct)
    {
        var counts = await Visible().AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byStatus = Enum.GetValues<RequestStatus>()
            .Where(workflow.IsVisibleToReviewers)
            .ToDictionary(s => JsonNamingPolicy.CamelCase.ConvertName(s.ToString()),
                s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);
        return Results.Ok(new ReviewSummary(byStatus.Values.Sum(), byStatus));
    }

    public async Task<IResult> ListAsync(
        string? status, string? service, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var query = Visible().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseVisibleStatus(status, out var s))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [ErrorCodes.UnknownOption] });
            query = query.Where(r => r.Status == s);
        }
        if (!string.IsNullOrWhiteSpace(service)) query = query.Where(r => r.ServiceCode == service.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(r => r.RequestNumber.ToUpper().Contains(term));
        }

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var current = Math.Max(page ?? 1, 1);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id)
            .Skip((current - 1) * size).Take(size)
            .Select(r => new
            {
                r.Id, r.RequestNumber, r.ServiceCode, r.Status, r.SubmittedAt, r.UpdatedAt, r.ApplicantUserId,
                City = r.Excavation!.IndustrialCityCode,
                Establishment = r.Establishment == null
                    ? null
                    : new EstablishmentInfo(r.Establishment.Id, r.Establishment.CrNumber, r.Establishment.NameAr, r.Establishment.NameEn),
            })
            .ToListAsync(ct);

        var applicants = await ApplicantsAsync(rows.Select(r => r.ApplicantUserId), ct);
        var names = await serviceNames.AllAsync(ct);
        var items = rows.Select(r =>
        {
            var (ar, en) = names.GetValueOrDefault(r.ServiceCode, (r.ServiceCode, r.ServiceCode));
            return new ReviewQueueItem(r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status, r.SubmittedAt,
                r.UpdatedAt, r.City, applicants[r.ApplicantUserId], r.Establishment);
        }).ToArray();

        return Results.Ok(new ReviewQueuePage(items, total, current, size));
    }

    public async Task<IResult> GetAsync(long id, CancellationToken ct)
    {
        var request = await LoadVisibleAsync(id, ct);
        return request is null ? Results.NotFound() : Results.Ok(await ToDetailAsync(request, ct));
    }

    public async Task<IResult> HistoryAsync(long id, CancellationToken ct)
    {
        var request = await LoadVisibleAsync(id, ct);
        return request is null ? Results.NotFound() : Results.Ok(await HistoryOfAsync(request, ct));
    }

    public async Task<IResult> DownloadAsync(long id, string slotCode, CancellationToken ct)
    {
        var request = await LoadVisibleAsync(id, ct);
        var attachment = request?.Attachments.FirstOrDefault(a => a.SlotCode == slotCode);
        var stream = attachment is null ? null : documents.OpenRead(attachment.StorageKey);
        if (stream is null) return Results.NotFound();
        return Results.File(stream, attachment!.ContentType, attachment.FileName);
    }

    public Task<IResult> ApproveAsync(UserAccess reviewer, long id, CommentBody body, CancellationToken ct)
        => DecideAsync(reviewer, id, RequestAction.Approve, "comment", body.Comment, textRequired: false, ct);

    public Task<IResult> ReturnAsync(UserAccess reviewer, long id, CommentBody body, CancellationToken ct)
        => DecideAsync(reviewer, id, RequestAction.ReturnForCompletion, "comment", body.Comment, textRequired: true, ct);

    public Task<IResult> RejectAsync(UserAccess reviewer, long id, RejectBody body, CancellationToken ct)
        => DecideAsync(reviewer, id, RequestAction.Reject, "reason", body.Reason, textRequired: true, ct);

    /// <summary>Asks the applicant for the service's coordination letters (optional instructions).</summary>
    public Task<IResult> RequestCoordinationLettersAsync(UserAccess reviewer, long id, CommentBody body, CancellationToken ct)
        => DecideAsync(reviewer, id, RequestAction.RequestCoordinationLetters, "comment", body.Comment, textRequired: false, ct);

    private async Task<IResult> DecideAsync(
        UserAccess reviewer, long id, RequestAction action, string field, string? text, bool textRequired, CancellationToken ct)
    {
        var request = await LoadVisibleAsync(id, ct);
        if (request is null) return Results.NotFound();

        var comment = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (comment is null && textRequired)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [ErrorCodes.Required] });
        if (comment?.Length > MaxCommentLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [ErrorCodes.TooLong] });

        // The workflow is service-agnostic; a decision the service has no use for (e.g. letters for a
        // service without coordination letters) is refused like any other illegal transition.
        if (!SupportedByService(request, action))
            return Results.Problem(title: ProblemCodes.InvalidTransition, statusCode: 409);

        try
        {
            await transitions.ApplyAsync(request, action, reviewer.Id, comment, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidTransitionException)
        {
            return Results.Problem(title: ProblemCodes.InvalidTransition, statusCode: 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another reviewer changed the status between our read and our write.
            return Results.Problem(title: ProblemCodes.RequestChanged, statusCode: 409);
        }

        logger.LogInformation("Request {Number}: {Action} by reviewer {Reviewer}", request.RequestNumber, action, reviewer.Id);
        return Results.Ok(await ToDetailAsync(request, ct));
    }

    // --- helpers -------------------------------------------------------------------------------

    private Task<ServiceRequest?> LoadVisibleAsync(long id, CancellationToken ct)
        => Visible()
            .Include(r => r.Excavation)
            .Include(r => r.Establishment)
            .Include(r => r.Attachments)
            .Include(r => r.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    private async Task<ReviewRequestDetail> ToDetailAsync(ServiceRequest r, CancellationToken ct)
    {
        var (ar, en) = await serviceNames.NameOfAsync(r.ServiceCode, ct);
        var service = ServiceApplications.Find(r.ServiceCode);
        var slots = service?.Slots ?? [];
        var letterSlots = service?.CoordinationSlots ?? [];
        var applicant = (await ApplicantsAsync([r.ApplicantUserId], ct))[r.ApplicantUserId];
        var establishment = r.Establishment is { } e ? new EstablishmentInfo(e.Id, e.CrNumber, e.NameAr, e.NameEn) : null;
        return new ReviewRequestDetail(
            r.Id, r.RequestNumber, r.ServiceCode, ar, en, r.Status, r.CreatedAt, r.UpdatedAt, r.SubmittedAt,
            applicant, establishment, ExcavationDto.From(r), slots, r.Attachments.InSlotOrder([.. slots, .. letterSlots]),
            await HistoryOfAsync(r, ct),
            workflow.ReviewActions(r.Status).Where(a => SupportedByService(r, a)).ToArray(),
            letterSlots);
    }

    private static bool SupportedByService(ServiceRequest r, RequestAction action)
        => action != RequestAction.RequestCoordinationLetters
           || ServiceApplications.Find(r.ServiceCode)?.CoordinationSlots.Length > 0;

    private async Task<ReviewHistoryItem[]> HistoryOfAsync(ServiceRequest r, CancellationToken ct)
    {
        var history = r.History.OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).ToList();
        var actors = await ApplicantsAsync(history.Select(h => h.ActorUserId), ct);
        return history.Select(h => new ReviewHistoryItem(
            h.FromStatus, h.ToStatus, h.Action, h.OccurredAt, h.Comment, h.ActorUserId,
            actors[h.ActorUserId].Email is { Length: > 0 } email ? email : null,
            h.ActorUserId == r.ApplicantUserId)).ToArray();
    }

    /// <summary>Looks up users in the FormCMS identity tables; unknown ids map to an empty record.</summary>
    private async Task<Dictionary<string, ApplicantInfo>> ApplicantsAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToArray();
        var found = await identityDb.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new ApplicantInfo(u.Id, u.Email ?? "", u.UserName ?? "", u.PhoneNumber))
            .ToDictionaryAsync(u => u.UserId, ct);
        return ids.ToDictionary(id => id, id => found.GetValueOrDefault(id) ?? new ApplicantInfo(id, "", "", null));
    }

    private bool TryParseVisibleStatus(string value, out RequestStatus status)
        => Enum.TryParse(value.Trim(), ignoreCase: true, out status)
           && !value.Trim().All(char.IsDigit)
           && Enum.IsDefined(status)
           && workflow.IsVisibleToReviewers(status);
}
