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
using Modon.Platform.Requests.Building;
using Modon.Platform.Requests.Workflow;
using Modon.Platform.Requests.Workflow.Camunda;
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
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ApplicantTaskKind>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<BuildingStage>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SelectionMode>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<TenderState>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<QuotationState>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ConstructionPhase>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<OfficeDecision>(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<PartnerRelation>(JsonNamingPolicy.CamelCase));
});
builder.Services.AddSingleton(TimeProvider.System);

// Workflow engine: InProcess (default; automated tests and local fallback) or Camunda (excavation BPMN in Camunda 8.9).
var workflowOptions = builder.Configuration.GetSection(WorkflowOptions.Section).Get<WorkflowOptions>() ?? new WorkflowOptions();
workflowOptions.Validate();
builder.Services.AddSingleton(workflowOptions);
builder.Services.AddSingleton<InProcessRequestWorkflow>();
if (workflowOptions.Engine == WorkflowEngineKind.Camunda)
{
    builder.Services.AddSingleton<ICamundaEngine>(sp => new CamundaSdkEngine(workflowOptions, sp.GetRequiredService<ILoggerFactory>()));
    builder.Services.AddSingleton<IRequestWorkflow, CamundaRequestWorkflow>();
}
else
{
    builder.Services.AddSingleton<IRequestWorkflow>(sp => sp.GetRequiredService<InProcessRequestWorkflow>());
}
builder.Services.AddSingleton<WorkflowDiagnostics>();
builder.Services.AddHostedService<WorkflowStartupCheck>();
builder.Services.AddHealthChecks().AddCheck<WorkflowHealthCheck>("workflow");
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<ExcavationValidator>();
builder.Services.AddSingleton<UploadValidator>();
builder.Services.AddScoped<RequestTransitions>();
builder.Services.AddScoped<ServiceCatalogNames>();
builder.Services.AddScoped<BuildingValidator>();
builder.Services.AddScoped<BuildingService>();
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
// "Healthy" / "Unhealthy" only (details: GET /api/modon/review/workflow). Never served from FormCMS's output cache.
app.MapHealthChecks("/health").CacheOutput(policy => policy.NoCache());

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
