using System.Net.Http.Json;
using FormCMS.Auth.Services;
using FormCMS.Utils.ResultExt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Http;
using Modon.Platform.Seeding;

namespace Modon.Platform.Tests;

/// <summary>
/// Boots the real Modon.Platform host against a throw-away SQLite file, so tests need no Docker.
/// </summary>
public class ModonAppFactory : WebApplicationFactory<ModonPlatformEntryPoint>
{
    public const string Password = DevUserSeeder.DefaultDevPassword;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"modon-test-{Guid.NewGuid():N}.db");

    /// <summary>Private document store for this test run (outside any web root).</summary>
    public string DocumentsRoot { get; } = Path.Combine(Path.GetTempPath(), $"modon-test-docs-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("Authentication:ApiKey", "test-api-key");
        builder.UseSetting("Modon:SeedDevUsers", "true");
        builder.UseSetting("Modon:DevUserPassword", Password);
        builder.UseSetting("Modon:Documents:Root", DocumentsRoot);
    }

    /// <summary>A client with its own cookie jar, so each test acts as one user.</summary>
    public HttpClient NewClient(bool withClientHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        if (withClientHeader) client.DefaultRequestHeaders.Add(ClientHeaderMiddleware.HeaderName, "tests");
        return client;
    }

    public async Task<HttpClient> LoginAs(string email)
    {
        var client = NewClient();
        var res = await client.PostAsJsonAsync("/api/login", new { usernameOrEmail = email, password = Password });
        res.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>
    /// Creates a user with the given roles and links them to a new establishment of their own (optionally registered
    /// as a consulting office or contractor, which only seeding can do).
    /// </summary>
    public async Task<(string Email, long EstablishmentId)> CreateUserAsync(string[] roles,
        DevUserSeeder.OfficeKind office = DevUserSeeder.OfficeKind.None)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAccountService>().EnsureUser(email, Password, roles).Ok();

        var userId = (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<FormCMS.Auth.Models.CmsUser>>()
            .FindByEmailAsync(email))!.Id;
        var cr = Random.Shared.NextInt64(7_000_000_000, 7_999_999_999).ToString();
        var establishment = await DevUserSeeder.EnsureEstablishmentMemberAsync(
            scope.ServiceProvider.GetRequiredService<ModonDbContext>(), cr, "منشأة اختبار", "Test establishment", userId, office);
        return (email, establishment.Id);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
        try { Directory.Delete(DocumentsRoot, recursive: true); } catch (IOException) { /* best effort */ }
    }
}

[CollectionDefinition("Modon API")]
public class ModonApiCollection : ICollectionFixture<ModonAppFactory>;
