using System.Net;
using System.Net.Http.Json;
using FormCMS.Auth.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Identity;
using Modon.Platform.Seeding;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>The seeded reviewer signs in with the documented dev credentials and reaches the review API.</summary>
[Collection("Modon API")]
public class ReviewerLoginTests(ModonAppFactory factory)
{
    private const string PersonasUrl = "/api/modon/me/personas";
    private const string SummaryUrl = "/api/modon/review/summary";

    [Fact]
    public async Task SeededReviewer_SignsInWithDevPassword_AndCanUseReviewApi()
    {
        var client = factory.NewClient();

        var login = await client.PostAsJsonAsync("/api/login",
            new { usernameOrEmail = "reviewer@modon.local", password = DevUserSeeder.DefaultDevPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await client.GetFromJsonAsync<PersonaResponse>(PersonasUrl);
        Assert.Equal([ModonRoles.ModonReviewer], me!.Roles);
        Assert.True(me.CanReview);
        Assert.False(me.CanUsePortal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SummaryUrl)).StatusCode);
    }

    [Fact]
    public async Task Reviewer_WithWrongPassword_Gets400_AndReviewApiStaysClosed()
    {
        var client = factory.NewClient();

        var login = await client.PostAsJsonAsync("/api/login",
            new { usernameOrEmail = "reviewer@modon.local", password = "wrong-password" });

        // The admin portal shows "check your email and password" only for 400/401 (anything else is a server problem).
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(SummaryUrl)).StatusCode);
    }

    [Fact]
    public async Task SignedInUserWithoutReviewerRole_IsForbiddenFromReviewApi()
    {
        var client = factory.NewClient();
        var email = $"new-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/register",
            new { email, password = ModonAppFactory.Password, userName = email })).EnsureSuccessStatusCode();
        var signedIn = await factory.LoginAs(email);

        var me = await signedIn.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        Assert.False(me!.CanReview);
        Assert.Equal(HttpStatusCode.Forbidden, (await signedIn.GetAsync(SummaryUrl)).StatusCode);
    }
}

/// <summary>Re-running the development seeder repairs demo accounts without resetting anything.</summary>
public class DevUserSeederTests
{
    [Fact]
    public async Task Rerun_RestoresMissingSeededRole_WithoutResettingPasswordsRolesOrData()
    {
        using var app = new ModonAppFactory();
        var contractor = await app.LoginAs("contractor@modon.local");
        var draft = await CreateDraft(contractor);

        const string changedPassword = "Changed@Pass9";
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
            var reviewer = (await users.FindByEmailAsync("reviewer@modon.local"))!;
            Assert.True((await users.RemoveFromRoleAsync(reviewer, ModonRoles.ModonReviewer)).Succeeded);
            Assert.True((await users.AddToRoleAsync(reviewer, ModonRoles.Consultant)).Succeeded);
            Assert.True((await users.ChangePasswordAsync(reviewer, ModonAppFactory.Password, changedPassword)).Succeeded);
        }
        var before = await CountsAsync(app);

        await DevUserSeeder.EnsureDevUsersAsync(app.Services, DevUserSeeder.DefaultDevPassword);
        await DevUserSeeder.EnsureDevUsersAsync(app.Services, DevUserSeeder.DefaultDevPassword);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
            var reviewer = (await users.FindByEmailAsync("reviewer@modon.local"))!;
            var roles = await users.GetRolesAsync(reviewer);
            Assert.Contains(ModonRoles.ModonReviewer, roles); // seeded role restored
            Assert.Contains(ModonRoles.Consultant, roles);    // role added by an administrator kept
            Assert.True(await users.CheckPasswordAsync(reviewer, changedPassword)); // password not reset
            Assert.False(await users.CheckPasswordAsync(reviewer, DevUserSeeder.DefaultDevPassword));
        }
        Assert.Equal(before, await CountsAsync(app)); // no users, establishments or requests added or removed
        Assert.Equal(HttpStatusCode.OK, (await contractor.GetAsync($"{RequestsUrl}/{draft.Id}")).StatusCode);
    }

    private static async Task<(int Users, int Establishments, int Members, int Requests)> CountsAsync(ModonAppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
        return (await users.Users.CountAsync(), await db.Establishments.CountAsync(),
            await db.EstablishmentMembers.CountAsync(), await db.ServiceRequests.CountAsync());
    }

    /// <summary>
    /// On Windows "localhost" resolves to IPv6 ::1 first, and Docker Desktop's IPv6 port forward can accept the
    /// connection and then never answer — the backend then times out at startup and every login fails.
    /// The dev connection string therefore names the IPv4 loopback explicitly.
    /// </summary>
    [Fact]
    public void DevelopmentPostgresConnection_UsesIpv4Loopback()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"), optional: false)
            .Build();

        var host = new Npgsql.NpgsqlConnectionStringBuilder(config.GetConnectionString("Postgres")).Host;

        Assert.Equal("127.0.0.1", host);
    }
}
