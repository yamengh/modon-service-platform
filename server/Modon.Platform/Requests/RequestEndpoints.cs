using FormCMS.Cms.Services;
using FormCMS.Core.Identities;
using Microsoft.AspNetCore.Mvc;

namespace Modon.Platform.Requests;

public static class RequestEndpoints
{
    /// <summary>Upload requests may carry one 20,480 KB file plus multipart overhead.</summary>
    private const long MaxUploadRequestBytes = ServiceApplications.MaxFileSizeKb * 1024L + 1024 * 1024;

    public static void MapRequestEndpoints(this WebApplication app)
    {
        var modon = app.MapGroup("/api/modon").AddEndpointFilter(RequireSignedIn);

        modon.MapGet("/me/establishments", (RequestService s, HttpContext ctx, CancellationToken ct)
            => s.MyEstablishmentsAsync(User(ctx), ct));

        var requests = modon.MapGroup("/requests");

        requests.MapPost("/", (RequestService s, HttpContext ctx, CreateRequestBody body, CancellationToken ct)
            => s.CreateAsync(User(ctx), body, ct));

        requests.MapGet("/", (RequestService s, HttpContext ctx, CancellationToken ct)
            => s.ListMineAsync(User(ctx), ct));

        requests.MapGet("/{id:long}", (RequestService s, HttpContext ctx, long id, CancellationToken ct)
            => s.GetAsync(User(ctx), id, ct));

        requests.MapPut("/{id:long}/excavation",
            (RequestService s, HttpContext ctx, long id, ExcavationDto body, CancellationToken ct)
                => s.SaveExcavationAsync(User(ctx), id, body, ct));

        requests.MapPost("/{id:long}/submit", (RequestService s, HttpContext ctx, long id, CancellationToken ct)
            => s.SubmitAsync(User(ctx), id, ct));

        // Multipart uploads are protected from cross-site posts by ClientHeaderMiddleware (custom header)
        // and the SameSite=Lax auth cookie, so ASP.NET's form antiforgery token is not used here.
        requests.MapPost("/{id:long}/attachments/{slot}",
                (RequestService s, HttpContext ctx, long id, string slot, IFormFile? file, CancellationToken ct)
                    => s.UploadAsync(User(ctx), id, slot, file, ct))
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes));

        requests.MapDelete("/{id:long}/attachments/{slot}",
            (RequestService s, HttpContext ctx, long id, string slot, CancellationToken ct)
                => s.DeleteAttachmentAsync(User(ctx), id, slot, ct));

        requests.MapGet("/{id:long}/attachments/{slot}",
            (RequestService s, HttpContext ctx, long id, string slot, CancellationToken ct)
                => s.DownloadAsync(User(ctx), id, slot, ct));
    }

    private const string UserKey = "modon.user";

    /// <summary>Returns 401 for anonymous callers and stores the FormCMS user for the handler.</summary>
    private static async ValueTask<object?> RequireSignedIn(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var user = http.RequestServices.GetRequiredService<IIdentityService>().GetUserAccess();
        if (user is null) return Results.Unauthorized();
        http.Items[UserKey] = user;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return await next(ctx);
    }

    private static UserAccess User(HttpContext ctx) => (UserAccess)ctx.Items[UserKey]!;
}
