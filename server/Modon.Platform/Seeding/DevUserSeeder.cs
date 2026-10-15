using FormCMS;
using FormCMS.Auth.Models;
using FormCMS.Auth.Services;
using FormCMS.Utils.ResultExt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;
using Modon.Platform.Organizations;

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

    /// <summary>Development establishments (commercial registrations) and the demo user who acts for each.</summary>
    public static readonly (string CrNumber, string NameAr, string NameEn, string MemberEmail)[] DevEstablishments =
    [
        ("7012345678", "مؤسسة البناء المتقدم للمقاولات", "Advanced Building Contracting Est.", "contractor@modon.local"),
        ("7098765432", "مصنع الشرق للصناعات", "Al-Sharq Industries Factory", "investor@modon.local"),
    ];

    /// <summary>Domain roles exist in every environment; they carry no entity rights yet.</summary>
    public static async Task EnsureRolesAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.EnsureRoles(ModonRoles.All).Ok();
    }

    /// <summary>Development-only demo users and establishments. Never enable in production.</summary>
    public static async Task EnsureDevUsersAsync(WebApplication app, string password)
    {
        foreach (var (email, roles) in DevUsers)
        {
            await app.EnsureCmsUser(email, password, roles).Ok();
        }

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ModonDbContext>();
        foreach (var (cr, nameAr, nameEn, email) in DevEstablishments)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is not null) await EnsureEstablishmentMemberAsync(db, cr, nameAr, nameEn, user.Id);
        }
    }

    /// <summary>Creates the establishment if needed and links the user to it. Idempotent.</summary>
    public static async Task<Establishment> EnsureEstablishmentMemberAsync(
        ModonDbContext db, string crNumber, string nameAr, string nameEn, string userId)
    {
        var establishment = await db.Establishments.Include(e => e.Members)
                                .FirstOrDefaultAsync(e => e.CrNumber == crNumber)
                            ?? db.Establishments.Add(new Establishment
                                { CrNumber = crNumber, NameAr = nameAr, NameEn = nameEn }).Entity;
        if (establishment.Members.All(m => m.UserId != userId))
            establishment.Members.Add(new EstablishmentMember { UserId = userId });
        await db.SaveChangesAsync();
        return establishment;
    }
}
