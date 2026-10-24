using FormCMS.Auth.Models;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Data;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Building;

namespace Modon.Platform.Notifications;

/// <summary>Why a user is notified.</summary>
public enum NotificationKind
{
    /// <summary>A status change (or monthly report) on the user's own request.</summary>
    RequestUpdate,

    /// <summary>An investor chose the user's office directly; it must accept or reject.</summary>
    OfficeInvited,

    /// <summary>The investor chose the office's quotation.</summary>
    QuotationSelected,

    /// <summary>The investor chose another quotation, or rejected all of them.</summary>
    QuotationNotSelected,

    /// <summary>Modon approved a project the office was selected for (the consulting office can now report).</summary>
    ProjectApproved,

    /// <summary>Modon rejected a project the office was selected for.</summary>
    ProjectRejected,
}

/// <summary>
/// An in-app notification for one user, created in the same database transaction as the status-history row it is
/// about. The unique index (history row, recipient) means a step can never notify the same person twice; a repeated
/// or conflicting action that is refused writes no history row and therefore no notification.
/// </summary>
public class ModonNotification
{
    public long Id { get; set; }
    public string RecipientUserId { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public long RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string ServiceCode { get; set; } = "";
    public long HistoryId { get; set; }
    public RequestStatusHistory? History { get; set; }
    public RequestAction Action { get; set; }
    public RequestStatus Status { get; set; }
    public string TitleAr { get; set; } = "";
    public string TitleEn { get; set; } = "";

    /// <summary>The comment of the step (e.g. Modon's return instructions or a rejection reason).</summary>
    public string? Comment { get; set; }

    /// <summary>Where the portal/app opens it: the request page for applicants, the project page for offices.</summary>
    public string Link { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>When the other channels (FormCMS inbox, e-mail and SMS stubs) handled it; null = pending.</summary>
    public DateTime? DispatchedAt { get; set; }
    public int DispatchAttempts { get; set; }
}

/// <summary>An office to notify about a building-licence step (all its members holding the stage's role).</summary>
public sealed record OfficeNotice(long EstablishmentId, BuildingStage Stage, NotificationKind Kind);

/// <summary>
/// Decides who hears about a status-history row and adds the notifications to the unit of work, so they are saved
/// atomically with the step itself.
/// </summary>
public sealed class NotificationPlanner(ModonDbContext db, CmsDbContext identityDb)
{
    /// <summary>Steps the applicant takes on their own request that still deserve a confirmation.</summary>
    private static readonly HashSet<RequestAction> Confirmations =
        [RequestAction.Submit, RequestAction.Resubmit, RequestAction.SubmitCoordinationLetters];

    public async Task<IReadOnlyList<ModonNotification>> PlanAsync(
        ServiceRequest r, RequestStatusHistory entry, IReadOnlyList<OfficeNotice>? offices, CancellationToken ct)
    {
        if (entry.Action == RequestAction.Create) return [];
        var planned = new List<ModonNotification>();

        // The applicant hears about every step on their request — except the ones they take themselves
        // (their own stage choices), apart from submission confirmations.
        if (entry.ActorUserId != r.ApplicantUserId || Confirmations.Contains(entry.Action))
            planned.Add(New(r, entry, r.ApplicantUserId, NotificationKind.RequestUpdate, $"/requests/{r.Id}"));

        var notices = new List<OfficeNotice>(offices ?? []);
        if (r.ServiceCode == ServiceCatalogData.BuildingLicenceCode && entry.Action is RequestAction.Approve or RequestAction.Reject)
        {
            var kind = entry.Action == RequestAction.Approve ? NotificationKind.ProjectApproved : NotificationKind.ProjectRejected;
            if (r.Building?.SelectedConsultantEstablishmentId is { } c) notices.Add(new OfficeNotice(c, BuildingStage.Consultant, kind));
            if (r.Building?.SelectedContractorEstablishmentId is { } k) notices.Add(new OfficeNotice(k, BuildingStage.Contractor, kind));
        }
        foreach (var notice in notices)
        {
            var role = notice.Stage == BuildingStage.Consultant ? ModonRoles.Consultant : ModonRoles.Contractor;
            foreach (var userId in await OfficeMembersAsync(notice.EstablishmentId, role, ct))
                planned.Add(New(r, entry, userId, notice.Kind, $"/projects/{r.Id}"));
        }

        // One notification per person per step, never to the person who acted (offices) — and never twice.
        var unique = planned
            .Where(n => n.Kind == NotificationKind.RequestUpdate || n.RecipientUserId != entry.ActorUserId)
            .GroupBy(n => n.RecipientUserId).Select(g => g.First())
            .ToList();
        db.Notifications.AddRange(unique);
        return unique;
    }

    /// <summary>Members of the office who hold the stage's role (users table of FormCMS / ASP.NET Identity).</summary>
    private async Task<List<string>> OfficeMembersAsync(long establishmentId, string role, CancellationToken ct)
    {
        var members = await db.EstablishmentMembers.AsNoTracking()
            .Where(m => m.EstablishmentId == establishmentId).Select(m => m.UserId).ToListAsync(ct);
        if (members.Count == 0) return members;
        return await identityDb.UserRoles.AsNoTracking()
            .Where(ur => members.Contains(ur.UserId))
            .Join(identityDb.Roles, ur => ur.RoleId, x => x.Id, (ur, x) => new { ur.UserId, x.Name })
            .Where(x => x.Name == role)
            .Select(x => x.UserId).Distinct().ToListAsync(ct);
    }

    private static ModonNotification New(ServiceRequest r, RequestStatusHistory entry, string recipient, NotificationKind kind, string link)
    {
        var (ar, en) = NotificationTexts.Title(kind, entry.Action, r.RequestNumber);
        return new ModonNotification
        {
            RecipientUserId = recipient, Kind = kind, RequestId = r.Id, RequestNumber = r.RequestNumber,
            ServiceCode = r.ServiceCode, History = entry, Action = entry.Action, Status = entry.ToStatus,
            TitleAr = ar, TitleEn = en, Comment = entry.Comment, Link = link, CreatedAt = entry.OccurredAt,
        };
    }
}

/// <summary>Bilingual titles, composed on the server so the portal, the app and the e-mail/SMS stubs say the same.</summary>
public static class NotificationTexts
{
    private static readonly Dictionary<RequestAction, (string Ar, string En)> RequestUpdates = new()
    {
        [RequestAction.Submit] = ("تم تقديم الطلب", "Request submitted"),
        [RequestAction.Resubmit] = ("تمت إعادة تقديم الطلب", "Request resubmitted"),
        [RequestAction.ReturnForCompletion] = ("أعادت مدن الطلب لإكمال النواقص", "Modon returned the request for completion"),
        [RequestAction.RequestCoordinationLetters] = ("طلبت مدن خطابات التنسيق", "Modon requested the coordination letters"),
        [RequestAction.SubmitCoordinationLetters] = ("تم إرسال خطابات التنسيق", "Coordination letters sent"),
        [RequestAction.Approve] = ("تم اعتماد الطلب", "Request approved"),
        [RequestAction.Reject] = ("تم رفض الطلب", "Request rejected"),
        [RequestAction.ConsultantAccepted] = ("قبل المكتب الاستشاري طلبك", "The consulting office accepted your request"),
        [RequestAction.ConsultantDeclined] = ("رفض المكتب الاستشاري طلبك", "The consulting office rejected your request"),
        [RequestAction.ContractorAccepted] = ("قبل المقاول طلبك", "The contractor accepted your request"),
        [RequestAction.ContractorDeclined] = ("رفض المقاول طلبك", "The contractor rejected your request"),
        [RequestAction.SubmitProgressReport] = ("تقرير متابعة بناء شهري جديد", "New monthly construction-progress report"),
    };

    private static readonly Dictionary<NotificationKind, (string Ar, string En)> OfficeKinds = new()
    {
        [NotificationKind.OfficeInvited] = ("اختارك مستثمر لطلب رخصة بناء — بانتظار ردك", "An investor chose your office for a building licence — please answer"),
        [NotificationKind.QuotationSelected] = ("تم اختيار عرض سعرك", "Your quotation was chosen"),
        [NotificationKind.QuotationNotSelected] = ("لم يتم اختيار عرض سعرك", "Your quotation was not chosen"),
        [NotificationKind.ProjectApproved] = ("اعتمدت مدن رخصة البناء لمشروعك", "Modon approved the building licence of your project"),
        [NotificationKind.ProjectRejected] = ("رفضت مدن رخصة البناء لمشروعك", "Modon rejected the building licence of your project"),
    };

    public static (string Ar, string En) Title(NotificationKind kind, RequestAction action, string requestNumber)
    {
        var (ar, en) = kind == NotificationKind.RequestUpdate
            ? RequestUpdates.GetValueOrDefault(action, ("تحديث على الطلب", "Request updated"))
            : OfficeKinds[kind];
        return ($"{ar} — {requestNumber}", $"{en} — {requestNumber}");
    }
}
