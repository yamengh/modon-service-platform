using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Modon.Platform.Requests.Building;

/// <summary>
/// Building licence endpoints. All of them sit behind the signed-in filter of <see cref="RequestEndpoints"/>;
/// role, ownership, office membership and status checks happen in <see cref="BuildingService"/>.
/// <list type="bullet">
/// <item>Investor: <c>/api/modon/requests/{id}/building/…</c> (stage choices and quotation decisions).</item>
/// <item>Offices: <c>/api/modon/building/projects/…</c> (open projects, invitations, quotations, progress reports).</item>
/// </list>
/// </summary>
public static class BuildingEndpoints
{
    public static void Map(RouteGroupBuilder modon, RouteGroupBuilder requests)
    {
        var investor = requests.MapGroup("/{id:long}/building");

        // "المشروع" or a specific office, for the consultant or contractor stage.
        investor.MapPost("/{stage}/selection", async (BuildingService b, RequestService r, HttpContext ctx, long id, string stage,
                SelectionBody body, CancellationToken ct)
            => ParseStage(stage) is { } kind
                ? await b.ChooseAsync(User(ctx), id, kind, body, ct) ?? await r.GetAsync(User(ctx), id, ct)
                : Results.NotFound());

        investor.MapPost("/{stage}/quotations/{quotationId:long}/accept", async (BuildingService b, RequestService r, HttpContext ctx,
                long id, string stage, long quotationId, CancellationToken ct)
            => ParseStage(stage) is { } kind
                ? await b.AcceptQuotationAsync(User(ctx), id, kind, quotationId, ct) ?? await r.GetAsync(User(ctx), id, ct)
                : Results.NotFound());

        investor.MapPost("/{stage}/quotations/reject-all", async (BuildingService b, RequestService r, HttpContext ctx,
                long id, string stage, InvestorCommentBody? body, CancellationToken ct)
            => ParseStage(stage) is { } kind
                ? await b.RejectQuotationsAsync(User(ctx), id, kind, body ?? new InvestorCommentBody(null), ct) ?? await r.GetAsync(User(ctx), id, ct)
                : Results.NotFound());

        // Files the investor may read: answers of offices and progress reports on their own request.
        investor.MapGet("/{kind:regex(^(quotations|progress-reports)$)}/{fileOwnerId:long}/attachment",
            async (BuildingService b, RequestService r, HttpContext ctx, long id, string kind, long fileOwnerId, CancellationToken ct)
                => await r.OwnsAsync(User(ctx), id, ct) ? await b.RequestFileAsync(id, kind, fileOwnerId, ct) : Results.NotFound());

        var building = modon.MapGroup("/building");

        building.MapGet("/offices", (BuildingService b, string? stage, CancellationToken ct)
            => ParseStage(stage ?? "") is { } kind ? b.OfficesAsync(kind, ct) : Task.FromResult(Results.NotFound()));

        var projects = building.MapGroup("/projects");

        projects.MapGet("/", (BuildingService b, HttpContext ctx, CancellationToken ct) => b.ProjectsAsync(User(ctx), ct));

        projects.MapGet("/{id:long}", (BuildingService b, HttpContext ctx, long id, CancellationToken ct)
            => b.ProjectAsync(User(ctx), id, ct));

        projects.MapPost("/{id:long}/quotations", (BuildingService b, HttpContext ctx, long id, QuotationBody body, CancellationToken ct)
            => b.SubmitQuotationAsync(User(ctx), id, body, ct));

        // JSON, or multipart form data with "data" (the JSON) and an optional "file".
        projects.MapPost("/{id:long}/response", async (BuildingService b, HttpContext ctx, long id, CancellationToken ct) =>
            {
                var (body, file, error) = await ReadPayloadAsync<OfficeResponseBody>(ctx, ct);
                return error ?? await b.RespondAsync(User(ctx), id, body!, file, ct);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(RequestEndpoints.MaxUploadRequestBytes));

        projects.MapPost("/{id:long}/progress-reports", async (BuildingService b, HttpContext ctx, long id, CancellationToken ct) =>
            {
                var (body, file, error) = await ReadPayloadAsync<ProgressReportBody>(ctx, ct);
                return error ?? await b.SubmitProgressReportAsync(User(ctx), id, body!, file, ct);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(RequestEndpoints.MaxUploadRequestBytes));

        projects.MapGet("/{id:long}/attachments/{slot}", (BuildingService b, HttpContext ctx, long id, string slot, CancellationToken ct)
            => b.PartnerDownloadAsync(User(ctx), id, slot, ct));

        projects.MapGet("/{id:long}/{kind:regex(^(quotations|progress-reports)$)}/{fileOwnerId:long}/attachment",
            (BuildingService b, HttpContext ctx, long id, string kind, long fileOwnerId, CancellationToken ct)
                => b.PartnerFileAsync(User(ctx), id, kind, fileOwnerId, ct));
    }

    public static BuildingStage? ParseStage(string value) => value switch
    {
        "consultant" => BuildingStage.Consultant,
        "contractor" => BuildingStage.Contractor,
        _ => null,
    };

    /// <summary>Reads a JSON body, or a multipart form with the JSON in "data" and an optional "file".</summary>
    private static async Task<(T? Body, IFormFile? File, IResult? Error)> ReadPayloadAsync<T>(HttpContext ctx, CancellationToken ct)
        where T : class
    {
        var json = ctx.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        try
        {
            if (ctx.Request.HasFormContentType)
            {
                var form = await ctx.Request.ReadFormAsync(ct);
                var data = form["data"].ToString();
                var body = string.IsNullOrWhiteSpace(data) ? null : JsonSerializer.Deserialize<T>(data, json);
                return body is null ? (null, null, Invalid()) : (body, form.Files.GetFile("file"), null);
            }
            var parsed = await ctx.Request.ReadFromJsonAsync<T>(json, ct);
            return parsed is null ? (null, null, Invalid()) : (parsed, null, null);
        }
        catch (JsonException)
        {
            return (null, null, Invalid());
        }

        static IResult Invalid() => Results.Problem(title: "invalidBody", statusCode: 400);
    }

    private static FormCMS.Core.Identities.UserAccess User(HttpContext ctx) => RequestEndpoints.User(ctx);
}
