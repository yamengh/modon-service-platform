using FormCMS;
using FormCMS.Auth.Models;
using FormCMS.Infrastructure.RelationDbDao;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Identity;
using Modon.Platform.Seeding;

var builder = WebApplication.CreateBuilder(args);

var dbProvider = builder.Configuration.GetValue<DatabaseProvider?>("Database:Provider")
                 ?? throw new Exception("Database:Provider is not configured (Postgres or Sqlite).");
var connStr = builder.Configuration.GetConnectionString(dbProvider.ToString())
              ?? throw new Exception($"ConnectionStrings:{dbProvider} is not configured.");
var apiKey = builder.Configuration.GetValue<string>("Authentication:ApiKey");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new Exception("Authentication:ApiKey is not configured.");

// ASP.NET Identity tables (users, roles) used by FormCMS auth.
builder.Services.AddDbContext<CmsDbContext>(options => _ = dbProvider switch
{
    DatabaseProvider.Postgres => options.UseNpgsql(connStr),
    DatabaseProvider.Sqlite => options.UseSqlite(connStr),
    _ => throw new NotSupportedException($"Database provider {dbProvider} is not supported by Modon.Platform.")
});

// FormCMS core: schemas, entities, saved queries, assets, GraphQL.
_ = dbProvider switch
{
    DatabaseProvider.Postgres => builder.Services.AddPostgresCms(connStr),
    _ => builder.Services.AddSqliteCms(connStr),
};

// FormCMS output-cache policies (required by UseCmsAsync).
builder.Services.AddOutputCache(cache =>
{
    cache.AddBasePolicy(p => p.Expire(TimeSpan.FromMinutes(1)));
    cache.AddPolicy(SystemSettings.PageCachePolicyName, p => p.Expire(TimeSpan.FromMinutes(1)));
    cache.AddPolicy(SystemSettings.QueryCachePolicyName, p => p.Expire(TimeSpan.FromSeconds(30)));
});

// FormCMS features kept on for later milestones.
builder.Services.AddCmsAuth<CmsUser, IdentityRole, CmsDbContext>(
    new AuthConfig(KeyAuthConfig: new KeyAuthConfig(apiKey)));
builder.Services.AddAuditLog();
builder.Services.AddNotify();

var app = builder.Build();

// Identity tables must be created before FormCMS creates its own tables.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.UseCmsAsync();
app.MapPersonaEndpoints();

await DevUserSeeder.EnsureRolesAsync(app);
await CatalogSeeder.SeedAsync(app.Services);
if (app.Configuration.GetValue<bool>("Modon:SeedDevUsers"))
{
    var password = app.Configuration.GetValue<string>("Modon:DevUserPassword") ?? DevUserSeeder.DefaultDevPassword;
    await DevUserSeeder.EnsureDevUsersAsync(app, password);
}

app.Run();

/// <summary>
/// Entry-point marker for WebApplicationFactory in tests. FormCMS also has a global
/// top-level <c>Program</c>, so tests cannot refer to <c>Program</c> unambiguously.
/// </summary>
public sealed class ModonPlatformEntryPoint;
