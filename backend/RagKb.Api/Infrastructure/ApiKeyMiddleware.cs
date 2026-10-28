using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RagKb.Api.Options;

namespace RagKb.Api.Infrastructure;

/// <summary>
/// Optional shared-secret gate for /api/*. Enabled only when Security:ApiKey is configured.
/// Good enough for an internal MVP / demo; replace with Entra ID / OIDC (JWT bearer) for real tenants.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public const string HeaderName = "X-Api-Key";
    private readonly byte[]? _expected = string.IsNullOrWhiteSpace(options.Value.ApiKey)
        ? null
        : Encoding.UTF8.GetBytes(options.Value.ApiKey);

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expected is null
            || !context.Request.Path.StartsWithSegments("/api")
            || HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        if (provided.Length == 0 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", status = 401, detail = $"Missing or invalid {HeaderName} header." });
            return;
        }

        await next(context);
    }
}
