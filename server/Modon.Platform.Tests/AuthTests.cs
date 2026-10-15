using System.Net;
using System.Net.Http.Json;
using Modon.Platform.Identity;

namespace Modon.Platform.Tests;

[Collection("Modon API")]
public class AuthTests(ModonAppFactory factory)
{
    private const string PersonasUrl = "/api/modon/me/personas";

    [Fact]
    public async Task Investor_Login_ReturnsInvestorPersona()
    {
        var client = await factory.LoginAs("investor@modon.local");

        var me = await client.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        Assert.NotNull(me);
        Assert.Equal("investor@modon.local", me.Email);
        Assert.Equal([ModonRoles.Investor], me.Personas);
        Assert.True(me.CanUsePortal);
    }

    [Theory]
    [InlineData("contractor@modon.local", ModonRoles.Contractor)]
    [InlineData("consultant@modon.local", ModonRoles.Consultant)]
    public async Task PartnerUsers_GetTheirOwnPersona(string email, string persona)
    {
        var client = await factory.LoginAs(email);

        var me = await client.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        Assert.Equal([persona], me!.Personas);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejected_AndNoSessionIsCreated()
    {
        var client = factory.NewClient();

        var res = await client.PostAsJsonAsync("/api/login",
            new { usernameOrEmail = "investor@modon.local", password = "wrong-password" });

        Assert.False(res.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(PersonasUrl)).StatusCode);
    }

    [Fact]
    public async Task Personas_WhenAnonymous_Returns401()
    {
        var res = await factory.NewClient().GetAsync(PersonasUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ModonReviewer_HasNoPortalPersona()
    {
        var client = await factory.LoginAs("reviewer@modon.local");

        var me = await client.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        Assert.Contains(ModonRoles.ModonReviewer, me!.Roles);
        Assert.Empty(me.Personas);
        Assert.False(me.CanUsePortal);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await factory.LoginAs("investor@modon.local");

        await client.GetAsync("/api/logout");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(PersonasUrl)).StatusCode);
    }

    [Fact]
    public async Task SelfRegisteredUser_GetsNoPortalPersona()
    {
        var client = factory.NewClient();
        var email = $"new-{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync("/api/register",
            new { email, password = ModonAppFactory.Password, userName = email });
        register.EnsureSuccessStatusCode();

        var loggedIn = await factory.LoginAs(email);
        var me = await loggedIn.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        Assert.Empty(me!.Personas);
        Assert.False(me.CanUsePortal);
    }

    [Fact]
    public async Task Investor_CannotGrantThemselvesRoles()
    {
        var client = await factory.LoginAs("investor@modon.local");
        var me = await client.GetFromJsonAsync<PersonaResponse>(PersonasUrl);

        var grantUser = await client.PostAsJsonAsync("/api/accounts/users", new
        {
            id = me!.Id, email = me.Email, name = me.Name, avatarUrl = "",
            roles = new[] { ModonRoles.Investor, ModonRoles.Contractor, "sa" },
            readWriteEntities = Array.Empty<string>(), readonlyEntities = Array.Empty<string>(),
            restrictedReadWriteEntities = Array.Empty<string>(), restrictedReadonlyEntities = Array.Empty<string>(),
            allowedMenus = Array.Empty<string>()
        });
        var editRole = await client.PostAsJsonAsync("/api/accounts/roles", new
        {
            name = ModonRoles.Investor, readWriteEntities = new[] { "modonService" },
            restrictedReadWriteEntities = Array.Empty<string>(), readonlyEntities = Array.Empty<string>(),
            restrictedReadonlyEntities = Array.Empty<string>()
        });

        Assert.False(grantUser.IsSuccessStatusCode);
        Assert.False(editRole.IsSuccessStatusCode);

        var after = await (await factory.LoginAs("investor@modon.local"))
            .GetFromJsonAsync<PersonaResponse>(PersonasUrl);
        Assert.Equal([ModonRoles.Investor], after!.Roles);
    }
}
