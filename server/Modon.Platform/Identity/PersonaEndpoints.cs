using FormCMS.Cms.Services;

namespace Modon.Platform.Identity;

public sealed record PersonaResponse(
    string Id,
    string Email,
    string Name,
    string[] Roles,
    string[] Personas,
    bool CanUsePortal,
    bool CanReview);

public static class PersonaEndpoints
{
    /// <summary>
    /// GET /api/modon/me/personas - which portal personas the signed-in user has.
    /// Personas are derived from roles on the server, so a client cannot claim one it was not granted.
    /// </summary>
    public static void MapPersonaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/modon/me/personas", (IIdentityService identityService) =>
        {
            var user = identityService.GetUserAccess();
            if (user is null) return Results.Unauthorized();

            var personas = ModonRoles.PersonasFor(user.Roles);
            return Results.Ok(new PersonaResponse(
                user.Id, user.Email, user.Name, user.Roles, personas, personas.Length > 0,
                ModonRoles.CanReview(user.Roles)));
        });
    }
}
