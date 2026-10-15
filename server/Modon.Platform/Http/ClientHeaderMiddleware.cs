using Modon.Platform.Requests;

namespace Modon.Platform.Http;

/// <summary>
/// CSRF defence for cookie-authenticated Modon endpoints: state-changing calls under /api/modon must
/// carry an "X-Modon-Client" header. Browsers cannot add custom headers to cross-site form posts
/// (and cross-origin fetches need a CORS preflight we don't allow), so forged requests are rejected.
/// </summary>
public static class ClientHeaderMiddleware
{
    public const string HeaderName = "X-Modon-Client";

    public static void UseModonClientHeader(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var req = ctx.Request;
            var isUnsafe = !(HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method) || HttpMethods.IsOptions(req.Method));
            if (isUnsafe && req.Path.StartsWithSegments("/api/modon")
                         && string.IsNullOrWhiteSpace(req.Headers[HeaderName]))
            {
                await Results.Problem(title: ProblemCodes.MissingClientHeader, statusCode: 400).ExecuteAsync(ctx);
                return;
            }
            await next();
        });
    }
}
