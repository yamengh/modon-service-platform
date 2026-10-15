using FormCMS;
using FormCMS.Auth.Models;
using FormCMS.Auth.Services;
using FormCMS.Utils.ResultExt;

namespace Modon.Platform.Seeding;

public static class DevUserSeeder
{
    public const string DefaultDevPassword = "Modon@Dev1";

    public static readonly (string Email, string[] Roles)[] DevUsers =
    [
        ("sadmin@modon.local", [Roles.Sa]),
        ("investor@modon.local", [ModonRoles.Investor]),
        ("contractor@modon.local", [ModonRoles.Contractor]),
        ("consultant@modon.local", [ModonRoles.Consultant]),
        ("reviewer@modon.local", [ModonRoles.ModonReviewer]),
    ];

    /// <summary>Domain roles exist in every environment; they carry no entity rights yet.</summary>
    public static async Task EnsureRolesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.EnsureRoles(ModonRoles.All).Ok();
    }

    /// <summary>Development-only demo users. Never enable in production.</summary>
    public static async Task EnsureDevUsersAsync(WebApplication app, string password)
    {
        foreach (var (email, roles) in DevUsers)
        {
            await app.EnsureCmsUser(email, password, roles).Ok();
        }
    }
}
