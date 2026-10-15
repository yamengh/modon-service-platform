using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Modon.Platform.Seeding;

namespace Modon.Platform.Tests;

/// <summary>
/// Boots the real Modon.Platform host against a throw-away SQLite file, so tests need no Docker.
/// </summary>
public class ModonAppFactory : WebApplicationFactory<ModonPlatformEntryPoint>
{
    public const string Password = DevUserSeeder.DefaultDevPassword;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"modon-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("Authentication:ApiKey", "test-api-key");
        builder.UseSetting("Modon:SeedDevUsers", "true");
        builder.UseSetting("Modon:DevUserPassword", Password);
    }

    /// <summary>A client with its own cookie jar, so each test acts as one user.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("http://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    public async Task<HttpClient> LoginAs(string email)
    {
        var client = NewClient();
        var res = await client.PostAsJsonAsync("/api/login", new { usernameOrEmail = email, password = Password });
        res.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }
}

[CollectionDefinition("Modon API")]
public class ModonApiCollection : ICollectionFixture<ModonAppFactory>;
