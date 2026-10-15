using Modon.Platform.Seeding;

namespace Modon.Platform.Catalog;

/// <summary>
/// Creates the catalogue entity, its two saved queries and the initial service records.
/// Idempotent: anything that already exists is left untouched.
/// </summary>
public static class CatalogSeeder
{
    public static Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
        => FormCmsSeeding.RunAsSystemAsync(services, async sp =>
        {
            await FormCmsSeeding.EnsureEntityAsync(sp, ServiceCatalogSchema.Entity, ct);
            await FormCmsSeeding.EnsureQueryAsync(sp, ServiceCatalogSchema.ListQueryName,
                ServiceCatalogSchema.EntityName, ServiceCatalogSchema.ListQuerySource, ct);
            await FormCmsSeeding.EnsureQueryAsync(sp, ServiceCatalogSchema.DetailQueryName,
                ServiceCatalogSchema.EntityName, ServiceCatalogSchema.DetailQuerySource, ct);
            await FormCmsSeeding.EnsureRecordsAsync(sp, ServiceCatalogSchema.EntityName, ["code"],
                ServiceCatalogData.Services, ct);
        });
}
