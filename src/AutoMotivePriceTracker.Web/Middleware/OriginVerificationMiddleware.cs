using System.Security.Cryptography;
using System.Text;

namespace AutoMotivePriceTracker.Web.Middleware;

/// <summary>
/// When a shared secret is configured (<c>Origin:SharedSecret</c>), only requests carrying it in the
/// <c>X-Origin-Verify</c> header are served. In AWS, CloudFront adds the header, so the API cannot be
/// reached by going around the CDN. Leave the secret empty (local dev, tests) to disable the check.
/// </summary>
public sealed class OriginVerificationMiddleware(RequestDelegate next, string secret)
{
    public const string HeaderName = "X-Origin-Verify";

    private readonly byte[] _secret = Encoding.UTF8.GetBytes(secret);

    public Task InvokeAsync(HttpContext context)
    {
        var supplied = Encoding.UTF8.GetBytes(context.Request.Headers[HeaderName].ToString());
        if (CryptographicOperations.FixedTimeEquals(supplied, _secret))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public static class OriginVerificationExtensions
{
    public static IApplicationBuilder UseOriginVerification(this IApplicationBuilder app, string? secret) =>
        string.IsNullOrEmpty(secret) ? app : app.UseMiddleware<OriginVerificationMiddleware>(secret);
}
