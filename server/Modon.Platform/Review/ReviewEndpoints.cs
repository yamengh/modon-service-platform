using FormCMS.Cms.Services;
using FormCMS.Core.Identities;
using Modon.Platform.Requests;

namespace Modon.Platform.Review;

/// <summary>
/// Endpoints for the Modon admin portal. Every call is checked on the server:
/// anonymous → 401, signed in without a reviewer role (<see cref="ModonRoles.ReviewerRoles"/>) → 403.
/// </summary>
public static class ReviewEndpoints
{
    private const string UserKey = "modon.reviewer";

    public static void MapReviewEndpoints(this WebApplication app)
    {
        var review = app.MapGroup("/api/modon/review").AddEndpointFilter(RequireReviewer);

        review.MapGet("/summary", (ReviewService s, CancellationToken ct) => s.SummaryAsync(ct));

        review.MapGet("/requests", (ReviewService s, string? status, string? service, string? search,
                int? page, int? pageSize, CancellationToken ct)
            => s.ListAsync(status, service, search, page, pageSize, ct));

        review.MapGet("/requests/{id:long}", (ReviewService s, long id, CancellationToken ct) => s.GetAsync(id, ct));

        review.MapGet("/requests/{id:long}/history", (ReviewService s, long id, CancellationToken ct)
            => s.HistoryAsync(id, ct));

        review.MapGet("/requests/{id:long}/attachments/{slot}", (ReviewService s, long id, string slot, CancellationToken ct)
            => s.DownloadAsync(id, slot, ct));

        review.MapPost("/requests/{id:long}/approve", (ReviewService s, HttpContext ctx, long id, CommentBody? body, CancellationToken ct)
            => s.ApproveAsync(Reviewer(ctx), id, body ?? new CommentBody(null), ct));

        review.MapPost("/requests/{id:long}/return", (ReviewService s, HttpContext ctx, long id, CommentBody? body, CancellationToken ct)
            => s.ReturnAsync(Reviewer(ctx), id, body ?? new CommentBody(null), ct));

        review.MapPost("/requests/{id:long}/reject", (ReviewService s, HttpContext ctx, long id, RejectBody? body, CancellationToken ct)
            => s.RejectAsync(Reviewer(ctx), id, body ?? new RejectBody(null), ct));
    }

    private static async ValueTask<object?> RequireReviewer(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var user = http.RequestServices.GetRequiredService<IIdentityService>().GetUserAccess();
        if (user is null) return Results.Unauthorized();
        if (!ModonRoles.CanReview(user.Roles)) return Results.Problem(title: ProblemCodes.RoleNotAllowed, statusCode: 403);
        http.Items[UserKey] = user;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return await next(ctx);
    }

    private static UserAccess Reviewer(HttpContext ctx) => (UserAccess)ctx.Items[UserKey]!;
}
