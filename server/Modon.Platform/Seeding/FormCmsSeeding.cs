using System.Security.Claims;
using System.Text.Json;
using FormCMS.Auth.Models;
using FormCMS.Cms.Services;
using FormCMS.Core.Descriptors;
using FormCMS.Utils.DisplayModels;
using Microsoft.Extensions.Primitives;

namespace Modon.Platform.Seeding;

/// <summary>
/// Idempotent helpers that create FormCMS entities, saved queries and records through FormCMS's own
/// services, so FormCMS hooks and validation still run. Existing items are never overwritten.
/// </summary>
public static class FormCmsSeeding
{
    /// <summary>
    /// FormCMS checks permissions through the current HttpContext user, so seeding runs as a
    /// temporary system principal (the same approach FormCMS.MonoApp uses).
    /// </summary>
    public static async Task RunAsSystemAsync(IServiceProvider services, Func<IServiceProvider, Task> action)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
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
            await action(sp);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public static async Task EnsureEntityAsync(IServiceProvider sp, Entity entity, CancellationToken ct)
    {
        var entitySchemas = sp.GetRequiredService<IEntitySchemaService>();
        var existing = await entitySchemas.LoadEntity(entity.Name, PublicationStatus.Published, ct);
        if (existing.IsSuccess) return;

        await entitySchemas.SaveTableDefine(
            new Schema(entity.Name, SchemaType.Entity, new Settings(Entity: entity)), asPublished: true, ct);
    }

    public static async Task EnsureQueryAsync(
        IServiceProvider sp, string name, string entityName, string source, CancellationToken ct)
    {
        var schemas = sp.GetRequiredService<ISchemaService>();
        if (await schemas.ByNameOrDefault(name, SchemaType.Query, PublicationStatus.Published, ct) is not null) return;

        var query = new Query(name, entityName, source, [], [], [], false);
        var saved = await sp.GetRequiredService<IQuerySchemaService>()
            .Save(new Schema(name, SchemaType.Query, new Settings(Query: query)), ct);
        await schemas.Publish(saved, ct);
    }

    /// <summary>Inserts the records whose key (built from <paramref name="keyFields"/>) is not present yet.</summary>
    public static async Task EnsureRecordsAsync(
        IServiceProvider sp, string entityName, string[] keyFields,
        IEnumerable<Dictionary<string, object>> records, CancellationToken ct)
    {
        var entities = sp.GetRequiredService<IEntityService>();
        var current = await entities.ListWithAction(entityName, ListResponseMode.Items,
            new Pagination(Limit: "1000"), new Dictionary<string, StringValues>(), ct);
        var existingKeys = (current?.Items ?? [])
            .Select(x => Key(keyFields.Select(f => x.TryGetValue(f, out var v) ? v : null)))
            .ToHashSet();

        foreach (var record in records)
        {
            if (existingKeys.Contains(Key(keyFields.Select(f => record[f])))) continue;
            await entities.InsertWithAction(entityName, JsonSerializer.SerializeToElement(record), ct);
        }

        static string Key(IEnumerable<object?> values) => string.Join("|", values.Select(v => v?.ToString()));
    }
}
