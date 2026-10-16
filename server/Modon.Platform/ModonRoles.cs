namespace Modon.Platform;

/// <summary>
/// Modon domain roles. They live next to the FormCMS system roles (sa, admin, user, guest).
/// Roles are only granted by seeding or by a FormCMS super admin; there is no self-service endpoint.
/// </summary>
public static class ModonRoles
{
    public const string Investor = "investor";
    public const string Contractor = "contractor";
    public const string Consultant = "consultant";
    public const string ModonReviewer = "modon_reviewer";

    public static readonly string[] All = [Investor, Contractor, Consultant, ModonReviewer];

    /// <summary>Personas that may use the partner (investor) web portal.</summary>
    public static readonly string[] PortalPersonas = [Investor, Contractor, Consultant];

    public static string[] PersonasFor(IEnumerable<string> roles)
        => PortalPersonas.Where(roles.Contains).ToArray();

    /// <summary>
    /// Roles allowed to use the admin portal and the review endpoints: Modon reviewers, plus the FormCMS
    /// super admin ("sa"), who already has unrestricted data access in FormCMS. The FormCMS "admin" role is
    /// a schema/content-editing role with no Modon meaning, so it is deliberately not included.
    /// </summary>
    public static readonly string[] ReviewerRoles = [ModonReviewer, FormCMS.Auth.Models.Roles.Sa];

    public static bool CanReview(IEnumerable<string> roles) => ReviewerRoles.Any(roles.Contains);
}
