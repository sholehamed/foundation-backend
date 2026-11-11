using System.Diagnostics;
using OpenTelemetry;

namespace Modules.System.Observability;

/// <summary>
/// Known-dangerous URL/HTTP/identity attributes are removed before an Activity
/// reaches any exporter. This is a defense in depth, not permission to record
/// secrets or arbitrary DTOs in source-level instrumentation.
/// </summary>
public sealed class FoundationTelemetryPrivacy : BaseProcessor<Activity>
{
    private static readonly HashSet<string> ForbiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "url.full", "url.path", "url.query", "url.fragment",
        "http.url", "http.target", "http.request.body", "http.response.body",
        "http.request.header.authorization", "http.request.header.cookie",
        "http.response.header.set_cookie", "http.request.header.x_api_key",
        "db.statement", "db.query.text", "db.connection_string",
        "exception.message", "exception.stacktrace",
        "enduser.id", "enduser.role", "user.id", "tenant.id"
    };

    public static bool CanTracePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        // OAuth/OIDC/login/first-password flows can carry secrets in URL and
        // protocol metadata. Do not export their request spans in FR-18B.
        return !path.StartsWith("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/connect/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/system/Auth/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/bff/auth/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase);
    }

    public override void OnEnd(Activity data)
    {
        foreach (var tag in data.TagObjects.ToArray())
        {
            if (ForbiddenTags.Contains(tag.Key) ||
                tag.Key.StartsWith("http.request.header.", StringComparison.OrdinalIgnoreCase) ||
                tag.Key.StartsWith("http.response.header.", StringComparison.OrdinalIgnoreCase) ||
                tag.Key.StartsWith("db.query.parameter.", StringComparison.OrdinalIgnoreCase) ||
                tag.Key.StartsWith("db.statement.parameter.", StringComparison.OrdinalIgnoreCase))
            {
                data.SetTag(tag.Key, null);
            }
        }

        // Never export a route with raw path/query parameters as span name.
        // ASP.NET Core's route template is sourced from server endpoint metadata.
        if (data.Source.Name == "Microsoft.AspNetCore")
        {
            var route = data.GetTagItem("http.route") as string;
            var method = data.GetTagItem("http.request.method") as string;
            data.DisplayName = $"{NormalizeMethod(method)} {(string.IsNullOrEmpty(route) ? "<unmatched>" : route)}";
        }
    }

    public static string NormalizeMethod(string? method) => method?.ToUpperInvariant() switch
    {
        "GET" => "GET", "HEAD" => "HEAD", "POST" => "POST", "PUT" => "PUT",
        "PATCH" => "PATCH", "DELETE" => "DELETE", "OPTIONS" => "OPTIONS",
        _ => "OTHER"
    };
}
