using System.Security.Claims;
using System.Text.Json;
using FormCMS.Auth.Models;
using FormCMS.Cms.Services;
using FormCMS.Core.Descriptors;
using FormCMS.Utils.DisplayModels;
using Microsoft.Extensions.Primitives;

namespace Modon.Platform.Catalog;

/// <summary>
/// Creates the catalogue entity, its two saved queries and the initial service records,
/// using FormCMS's own services so all FormCMS hooks and validation still run.
/// Idempotent: anything that already exists is left untouched.
/// </summary>
public static class CatalogSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // FormCMS checks permissions through the current HttpContext user, so seeding runs
        // as a temporary system principal (the same approach FormCMS.MonoApp uses).
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = sp,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "modon-seed"),
                new Claim(ClaimTypes.Name, "ModonSeeder"),
                new Claim(ClaimTypes.Role, Roles.Sa),
            ], "Seeding"))
        };

        try
        {
            await EnsureEntity(sp.GetRequiredService<IEntitySchemaService>(), ct);
            await EnsureQuery(sp, ServiceCatalogSchema.ListQueryName, ServiceCatalogSchema.ListQuerySource, ct);
            await EnsureQuery(sp, ServiceCatalogSchema.DetailQueryName, ServiceCatalogSchema.DetailQuerySource, ct);
            await EnsureServices(sp.GetRequiredService<IEntityService>(), ct);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task EnsureEntity(IEntitySchemaService entitySchemas, CancellationToken ct)
    {
        var existing = await entitySchemas.LoadEntity(ServiceCatalogSchema.EntityName, PublicationStatus.Published, ct);
        if (existing.IsSuccess) return;

        var schema = new Schema(ServiceCatalogSchema.EntityName, SchemaType.Entity,
            new Settings(Entity: ServiceCatalogSchema.Entity));
        await entitySchemas.SaveTableDefine(schema, asPublished: true, ct);
    }

    private static async Task EnsureQuery(IServiceProvider sp, string name, string source, CancellationToken ct)
    {
        var schemas = sp.GetRequiredService<ISchemaService>();
        if (await schemas.ByNameOrDefault(name, SchemaType.Query, PublicationStatus.Published, ct) is not null) return;

        var query = new Query(name, ServiceCatalogSchema.EntityName, source, [], [], [], false);
        var saved = await sp.GetRequiredService<IQuerySchemaService>()
            .Save(new Schema(name, SchemaType.Query, new Settings(Query: query)), ct);
        await schemas.Publish(saved, ct);
    }

    private static async Task EnsureServices(IEntityService entities, CancellationToken ct)
    {
        var current = await entities.ListWithAction(ServiceCatalogSchema.EntityName, ListResponseMode.Items,
            new Pagination(Limit: "1000"), new Dictionary<string, StringValues>(), ct);
        var existingCodes = (current?.Items ?? [])
            .Select(x => x.TryGetValue("code", out var c) ? c?.ToString() : null)
            .ToHashSet();

        foreach (var service in ServiceCatalogData.Services)
        {
            if (existingCodes.Contains(service["code"].ToString())) continue;
            await entities.InsertWithAction(ServiceCatalogSchema.EntityName,
                JsonSerializer.SerializeToElement(service), ct);
        }
    }
}
