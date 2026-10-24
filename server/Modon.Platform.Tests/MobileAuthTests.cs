using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FormCMS.Auth.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modon.Platform.Data;
using Modon.Platform.Identity;
using Modon.Platform.Requests;
using static Modon.Platform.Tests.RequestTestKit;

namespace Modon.Platform.Tests;

/// <summary>Mobile sign-in with user-specific bearer tokens, next to the unchanged web cookie.</summary>
[Collection("Modon API")]
public class MobileAuthTests(ModonAppFactory factory)
{
    private const string SessionUrl = "/api/modon/mobile/session";

    /// <summary>A client like the app: no cookies at all, the client header, optionally a bearer token.</summary>
    private HttpClient App(string? token = null)
    {
        var client = factory.NewClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpResponseMessage> SignIn(string email, string password = ModonAppFactory.Password)
        => await App().PostAsJsonAsync(SessionUrl, new { email, password, deviceName = "Pixel test" });

    private async Task<string> TokenFor(string email)
    {
        var res = await SignIn(email);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<MobileLoginResponse>(Json))!.Token;
    }

    private async Task Expire(string token, Action<MobileSession> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
        var hash = MobileAuth.Hash(token);
        var session = await db.MobileSessions.SingleAsync(s => s.TokenHash == hash);
        change(session);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ValidLogin_IssuesAUserSpecificToken_WithoutACookie()
    {
        var res = await SignIn("contractor@modon.local");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Set-Cookie"));
        var body = (await res.Content.ReadFromJsonAsync<MobileLoginResponse>(Json))!;
        Assert.StartsWith(MobileAuth.TokenPrefix, body.Token);
        Assert.True(body.Token.Length > 40);
        Assert.InRange(body.ExpiresAt, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));
        Assert.Equal("contractor@modon.local", body.User.Email);
        Assert.Equal(["contractor"], body.User.Personas);

        var me = await App(body.Token).GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas", Json);
        Assert.Equal("contractor@modon.local", me!.Email);
        Assert.Equal(HttpStatusCode.OK, (await App(body.Token).GetAsync(RequestsUrl)).StatusCode);

        // Only the hash is stored.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
        Assert.False(await db.MobileSessions.AnyAsync(s => s.TokenHash == body.Token));
        Assert.True(await db.MobileSessions.AnyAsync(s => s.TokenHash == MobileAuth.Hash(body.Token) && s.DeviceName == "Pixel test"));
    }

    [Fact]
    public async Task InvalidLogin_IsRefusedWithoutSayingWhichPartWasWrong()
    {
        foreach (var (email, password) in new[] { ("contractor@modon.local", "wrong"), ("nobody@modon.local", "x"), ("", "") })
        {
            var res = await SignIn(email, password);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Equal("invalidCredentials", await ProblemTitle(res));
        }
        var noHeader = factory.NewClient(withClientHeader: false);
        var refused = await noHeader.PostAsJsonAsync(SessionUrl, new { email = "contractor@modon.local", password = ModonAppFactory.Password });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheTokenOnTheServer()
    {
        var token = await TokenFor("investor@modon.local");

        Assert.Equal(HttpStatusCode.NoContent, (await App(token).DeleteAsync(SessionUrl)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await App(token).GetAsync("/api/modon/me/personas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App(token).DeleteAsync(SessionUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App().DeleteAsync(SessionUrl)).StatusCode);
    }

    [Fact]
    public async Task SessionRestore_TheStoredTokenKeepsWorkingAfterAnAppRestart()
    {
        var token = await TokenFor("consultant@modon.local");

        // A brand-new client (the app after a restart) with only the token from secure storage.
        var restored = await App(token).GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas", Json);

        Assert.Equal(["consultant"], restored!.Personas);
        Assert.True(restored.CanUsePortal);
    }

    [Fact]
    public async Task ExpiredRevokedTamperedOrStaleTokens_AreUnauthorized()
    {
        var expired = await TokenFor("investor@modon.local");
        await Expire(expired, s => s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        var revoked = await TokenFor("investor@modon.local");
        await Expire(revoked, s => s.RevokedAt = DateTime.UtcNow);
        var tampered = (await TokenFor("investor@modon.local"))[..^2] + "xx";

        foreach (var token in new[] { expired, revoked, tampered, "mdn_not-a-real-token" })
        {
            var res = await App(token).GetAsync("/api/modon/me/personas");
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await App(token).GetAsync(RequestsUrl)).StatusCode);
        }

        // A password change (new security stamp) ends existing mobile sessions.
        var (email, _) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var stale = await TokenFor(email);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.ChangePasswordAsync(user, ModonAppFactory.Password, "Changed@Pass1");
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await App(stale).GetAsync("/api/modon/me/personas")).StatusCode);
    }

    [Fact]
    public async Task RoleRestrictions_AreEnforcedOnTheServer_ForMobileTokens()
    {
        // Reviewers and the super admin use the admin web portal; the app gives them no token.
        foreach (var email in new[] { "reviewer@modon.local", "sadmin@modon.local" })
        {
            var res = await SignIn(email);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal(ProblemCodes.RoleNotAllowed, await ProblemTitle(res));
        }

        var contractor = App(await TokenFor("contractor@modon.local"));
        var investor = App(await TokenFor("investor@modon.local"));
        Assert.Equal(HttpStatusCode.Forbidden, (await contractor.GetAsync("/api/modon/review/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await contractor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "building-licence" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await investor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "excavation-permit" })).StatusCode);

        // Ownership: an investor's draft is invisible to another user's token.
        var draft = await Read(await investor.PostAsJsonAsync(RequestsUrl, new { serviceCode = "building-licence" }));
        Assert.Equal(HttpStatusCode.NotFound, (await contractor.GetAsync($"{RequestsUrl}/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task TheSharedApiKey_IsNotAMobileCredential()
    {
        var withKey = factory.NewClient();
        withKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await withKey.GetAsync("/api/modon/me/personas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withKey.GetAsync(RequestsUrl)).StatusCode);
    }

    [Fact]
    public async Task MobileTokens_CanFillAndSubmitAnExcavationRequest_IncludingUploads()
    {
        var (email, establishmentId) = await factory.CreateUserAsync([ModonRoles.Contractor]);
        var app = App(await TokenFor(email));

        var draft = await CreateDraft(app);
        await Read(await SaveExcavation(app, draft.Id, ValidExcavation(establishmentId)));
        await UploadAllRequired(app, draft.Id);
        var submitted = await Read(await Submit(app, draft.Id));

        Assert.Equal(RequestStatus.InProgress, submitted.Status);
        var download = await app.GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await App(await TokenFor("contractor@modon.local")).GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App().GetAsync($"{RequestsUrl}/{draft.Id}/attachments/workRoutePlan")).StatusCode);
    }

    [Fact]
    public async Task WebCookieSignIn_IsUnchanged()
    {
        var web = await factory.LoginAs("investor@modon.local");
        var me = await web.GetFromJsonAsync<PersonaResponse>("/api/modon/me/personas", Json);
        Assert.Equal("investor@modon.local", me!.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/modon/me/personas")).StatusCode);
    }
}
