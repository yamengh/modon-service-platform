using System.Text.Json;
using System.Text.Json.Serialization;
using FormCMS;
using FormCMS.Auth.Models;
using FormCMS.Infrastructure.RelationDbDao;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Catalog;
using Modon.Platform.Data;
using Modon.Platform.Documents;
using Modon.Platform.Http;
using Modon.Platform.Identity;
using Modon.Platform.Lookups;
using Modon.Platform.Requests;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Review;
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

// Modon workflow data (requests, history, attachments, establishments) — own EF Core context.
builder.Services.AddModonDbContext(dbProvider, connStr);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<RequestStatus>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<RequestAction>(JsonNamingPolicy.CamelCase));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRequestWorkflow, InProcessRequestWorkflow>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<ExcavationValidator>();
builder.Services.AddSingleton<UploadValidator>();
builder.Services.AddScoped<RequestTransitions>();
builder.Services.AddScoped<ServiceCatalogNames>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<ReviewService>();

// Private documents live outside wwwroot, so they are never served as static files.
var documentsRoot = builder.Configuration.GetValue<string>("Modon:Documents:Root")
                    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "private-documents");
builder.Services.AddSingleton<IPrivateDocumentStore>(new LocalPrivateDocumentStore(documentsRoot));

var app = builder.Build();

// Identity tables must be created before FormCMS creates its own tables.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.UseCmsAsync();
await app.EnsureModonSchemaAsync(dbProvider);

app.UseModonClientHeader();
app.MapPersonaEndpoints();
app.MapRequestEndpoints();
app.MapReviewEndpoints();

await DevUserSeeder.EnsureRolesAsync(app);
await CatalogSeeder.SeedAsync(app.Services);
await LookupSeeder.SeedAsync(app.Services);
if (app.Configuration.GetValue<bool>("Modon:SeedDevUsers"))
{
    var password = app.Configuration.GetValue<string>("Modon:DevUserPassword") ?? DevUserSeeder.DefaultDevPassword;
    await DevUserSeeder.EnsureDevUsersAsync(app.Services, password);
}

app.Run();

/// <summary>
/// Entry-point marker for WebApplicationFactory in tests. FormCMS also has a global
/// top-level <c>Program</c>, so tests cannot refer to <c>Program</c> unambiguously.
/// </summary>
public sealed class ModonPlatformEntryPoint;
