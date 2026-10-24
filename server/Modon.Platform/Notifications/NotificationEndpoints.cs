using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;
using Modon.Platform.Requests;

namespace Modon.Platform.Notifications;

public sealed record NotificationView(
    long Id,
    NotificationKind Kind,
    long RequestId,
    string RequestNumber,
    string ServiceCode,
    RequestAction Action,
    RequestStatus Status,
    string TitleAr,
    string TitleEn,
    string? Comment,
    string Link,
    DateTime CreatedAt,
    bool Read);

public sealed record NotificationPage(NotificationView[] Items, int Total, int UnreadCount, int Page, int PageSize);

/// <summary>
/// The signed-in user's notifications (web portal and mobile app). Every query is filtered by the caller's own user id,
/// so nobody can list, count or mark someone else's notifications (an unknown or foreign id is 404).
/// </summary>
public static class NotificationEndpoints
{
    public const int PageSize = 20;

    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/modon/notifications").AddEndpointFilter(RequestEndpoints.RequireSignedIn);

        group.MapGet("/", async (HttpContext ctx, ModonDbContext db, int? page, bool? unreadOnly, CancellationToken ct) =>
        {
            var userId = RequestEndpoints.User(ctx).Id;
            var mine = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
            var query = unreadOnly == true ? mine.Where(n => n.ReadAt == null) : mine;
            var current = Math.Max(page ?? 1, 1);
            var total = await query.CountAsync(ct);
            var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
            var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Skip((current - 1) * PageSize).Take(PageSize)
                .Select(n => new NotificationView(n.Id, n.Kind, n.RequestId, n.RequestNumber, n.ServiceCode, n.Action, n.Status,
                    n.TitleAr, n.TitleEn, n.Comment, n.Link, n.CreatedAt, n.ReadAt != null))
                .ToArrayAsync(ct);
            return Results.Ok(new NotificationPage(items, total, unread, current, PageSize));
        });

        group.MapGet("/unread-count", async (HttpContext ctx, ModonDbContext db, CancellationToken ct) =>
        {
            var userId = RequestEndpoints.User(ctx).Id;
            return Results.Ok(new { unreadCount = await db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct) });
        });

        group.MapPost("/{id:long}/read", async (HttpContext ctx, ModonDbContext db, TimeProvider clock, long id, CancellationToken ct) =>
        {
            var userId = RequestEndpoints.User(ctx).Id;
            var now = clock.GetUtcNow().UtcDateTime;
            var exists = await db.Notifications.AnyAsync(n => n.Id == id && n.RecipientUserId == userId, ct);
            if (!exists) return Results.NotFound();
            await db.Notifications.Where(n => n.Id == id && n.RecipientUserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", async (HttpContext ctx, ModonDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = RequestEndpoints.User(ctx).Id;
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Notifications.Where(n => n.RecipientUserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });
    }
}
