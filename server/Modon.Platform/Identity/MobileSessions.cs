using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FormCMS.Auth.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modon.Platform.Data;

namespace Modon.Platform.Identity;

/// <summary>
/// A signed-in mobile device. The bearer token itself is never stored: only its SHA-256 hash, so a database
/// leak does not reveal usable tokens. Tokens are user-specific, expire, and are revoked on logout or when the
/// user's security stamp changes (e.g. a password change).
/// </summary>
public class MobileSession
{
    public long Id { get; set; }
    public string TokenHash { get; set; } = "";
    public string UserId { get; set; } = "";

    /// <summary>The user's ASP.NET Identity security stamp when the token was issued.</summary>
    public string SecurityStamp { get; set; } = "";

    public string? DeviceName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Configuration section <c>Modon:MobileAuth</c>.</summary>
public sealed class MobileAuthOptions
{
    public const string Section = "Modon:MobileAuth";

    /// <summary>How long a mobile sign-in lasts before the user must sign in again.</summary>
    public int TokenLifetimeDays { get; set; } = 30;
}

public static class MobileAuth
{
    /// <summary>Authentication scheme that validates Modon mobile tokens.</summary>
    public const string Scheme = "ModonMobileBearer";

    /// <summary>Policy scheme used as the default: mobile token when one is sent, otherwise the FormCMS cookie.</summary>
    public const string AutoScheme = "ModonCookieOrMobile";

    /// <summary>
    /// Every Modon token starts with this prefix. It keeps them apart from FormCMS's shared API key, which also travels
    /// as "Bearer …" but is never accepted here and never given to the app.
    /// </summary>
    public const string TokenPrefix = "mdn_";

    public static bool HasMobileToken(HttpContext ctx, out string token)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        token = header.StartsWith("Bearer " + TokenPrefix, StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : "";
        return token.Length > 0;
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Adds the mobile bearer scheme next to FormCMS's cookie scheme without changing FormCMS: the default
    /// authenticate scheme becomes a policy scheme that forwards to the mobile handler only when the request carries
    /// a Modon token, and to the Identity cookie otherwise — web sign-in works exactly as before.
    /// </summary>
    public static void AddModonMobileAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MobileAuthOptions>(config.GetSection(MobileAuthOptions.Section));
        services.AddScoped<MobileTokenService>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, MobileBearerHandler>(Scheme, null)
            .AddPolicyScheme(AutoScheme, AutoScheme, o =>
                o.ForwardDefaultSelector = ctx => HasMobileToken(ctx, out _) ? Scheme : IdentityConstants.ApplicationScheme);
        // AddIdentity (inside FormCMS) sets the cookie as default authenticate scheme; run after it.
        services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = AutoScheme);
    }
}

/// <summary>Issues, validates and revokes mobile tokens.</summary>
public sealed class MobileTokenService(
    ModonDbContext db,
    UserManager<CmsUser> users,
    SignInManager<CmsUser> signIn,
    IOptions<MobileAuthOptions> options,
    TimeProvider clock)
{
    public sealed record Issued(string Token, DateTime ExpiresAt);

    /// <summary>
    /// Checks the password (with ASP.NET Identity lockout) and issues a token. Returns null for wrong credentials;
    /// <paramref name="lockedOut"/> tells the caller when the account is temporarily locked.
    /// </summary>
    public async Task<(CmsUser? User, bool LockedOut)> CheckCredentialsAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
        if (user is null) return (null, false);
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        return result.Succeeded ? (user, false) : (null, result.IsLockedOut);
    }

    public async Task<Issued> IssueAsync(CmsUser user, string? deviceName, CancellationToken ct)
    {
        var token = MobileAuth.TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow().UtcDateTime;
        var session = new MobileSession
        {
            TokenHash = MobileAuth.Hash(token),
            UserId = user.Id,
            SecurityStamp = await users.GetSecurityStampAsync(user),
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim()[..Math.Min(100, deviceName.Trim().Length)],
            CreatedAt = now,
            ExpiresAt = now.AddDays(Math.Clamp(options.Value.TokenLifetimeDays, 1, 365)),
        };
        db.MobileSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new Issued(token, session.ExpiresAt);
    }

    /// <summary>The user behind a valid token, or null (unknown, expired, revoked, or the password changed since).</summary>
    public async Task<CmsUser?> ValidateAsync(string token, CancellationToken ct)
    {
        var hash = MobileAuth.Hash(token);
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.MobileSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now) return null;
        var user = await users.FindByIdAsync(session.UserId);
        if (user is null || await users.GetSecurityStampAsync(user) != session.SecurityStamp) return null;
        if (await users.IsLockedOutAsync(user)) return null;

        // Record use at most once a minute, to keep reads cheap.
        if (session.LastUsedAt is null || now - session.LastUsedAt > TimeSpan.FromMinutes(1))
            await db.MobileSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastUsedAt, now), ct);
        return user;
    }

    /// <summary>Same claims as a cookie sign-in (roles, entity rights), built by FormCMS's principal factory.</summary>
    public Task<ClaimsPrincipal> PrincipalAsync(CmsUser user) => signIn.CreateUserPrincipalAsync(user);

    public async Task<bool> RevokeAsync(string token, CancellationToken ct)
    {
        var hash = MobileAuth.Hash(token);
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.MobileSessions.Where(s => s.TokenHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct) > 0;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Authenticates "Authorization: Bearer mdn_…" requests as the token's user.</summary>
public sealed class MobileBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    MobileTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!MobileAuth.HasMobileToken(Context, out var token)) return AuthenticateResult.NoResult();
        var user = await tokens.ValidateAsync(token, Context.RequestAborted);
        if (user is null) return AuthenticateResult.Fail("The mobile session is invalid or has expired.");
        var principal = await tokens.PrincipalAsync(user);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Task.CompletedTask;
    }
}

public sealed record MobileLoginBody(string? Email, string? Password, string? DeviceName);

/// <summary>A new mobile session: the token (shown once) and who signed in.</summary>
public sealed record MobileLoginResponse(string Token, DateTime ExpiresAt, PersonaResponse User);

public static class MobileSessionEndpoints
{
    /// <summary>
    /// POST /api/modon/mobile/session — sign in from the app (e-mail + password) and receive a bearer token.
    /// DELETE /api/modon/mobile/session — sign out: the token is revoked on the server.
    /// The app stores the token in the device's secure storage. Only users with a partner-portal persona
    /// (investor, contractor, consultant) get one; reviewers use the admin web portal.
    /// </summary>
    public static void MapMobileSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/modon/mobile/session");

        group.MapPost("/", async (MobileLoginBody body, MobileTokenService tokens, UserManager<CmsUser> users, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrEmpty(body.Password))
                return Results.Problem(title: "invalidCredentials", statusCode: 401);
            var (user, lockedOut) = await tokens.CheckCredentialsAsync(body.Email.Trim(), body.Password);
            if (user is null) return Results.Problem(title: lockedOut ? "lockedOut" : "invalidCredentials", statusCode: 401);

            var roles = (await users.GetRolesAsync(user)).ToArray();
            var personas = ModonRoles.PersonasFor(roles);
            if (personas.Length == 0) return Results.Problem(title: Requests.ProblemCodes.RoleNotAllowed, statusCode: 403);

            var issued = await tokens.IssueAsync(user, body.DeviceName, ct);
            return Results.Ok(new MobileLoginResponse(issued.Token, issued.ExpiresAt, new PersonaResponse(
                user.Id, user.Email ?? "", user.UserName ?? "", roles, personas, true, ModonRoles.CanReview(roles))));
        });

        group.MapDelete("/", async (HttpContext ctx, MobileTokenService tokens, CancellationToken ct) =>
        {
            if (!MobileAuth.HasMobileToken(ctx, out var token) || ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            await tokens.RevokeAsync(token, ct);
            return Results.NoContent();
        });
    }
}
