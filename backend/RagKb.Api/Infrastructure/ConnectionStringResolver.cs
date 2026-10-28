using Npgsql;

namespace RagKb.Api.Infrastructure;

/// <summary>
/// Render, Railway, Neon and Heroku hand out URLs like postgres://user:pass@host:5432/db?sslmode=require,
/// which Npgsql does not accept. This converts them; key=value strings pass through untouched.
/// </summary>
public static class ConnectionStringResolver
{
    public static string Resolve(IConfiguration config)
    {
        // DATABASE_URL (set by hosting platforms) wins over the local default in appsettings.json.
        var raw = NullIfEmpty(config["DATABASE_URL"])
                  ?? NullIfEmpty(config.GetConnectionString("Postgres"))
                  ?? throw new InvalidOperationException(
                      "No database configured. Set ConnectionStrings__Postgres or DATABASE_URL.");

        return Normalize(raw);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string Normalize(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Public managed hosts (Neon, Render external) require TLS. Local docker and private-network
            // hostnames without a dot (Render internal URL, e.g. dpg-abc123-a) use TLS only if offered.
            SslMode = uri.Host is "localhost" or "127.0.0.1" ? SslMode.Disable
                    : uri.Host.Contains('.') ? SslMode.Require
                    : SslMode.Prefer,
        };

        // Honour query-string overrides such as ?sslmode=disable
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["sslmode"] is { } sslMode && Enum.TryParse<SslMode>(sslMode.Replace("-", ""), true, out var parsed))
            builder.SslMode = parsed;

        return builder.ConnectionString;
    }
}
