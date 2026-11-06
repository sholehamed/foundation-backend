namespace Modules.System.Bff;

public sealed record BffHostContext(string LoginContext, string? TenantSlug, string Host);

/// <summary>
/// Resolves the System/Tenant login context from the host that owns the BFF
/// cookie. Query-string tenant values are never used as the source of truth.
/// </summary>
public static class BffHostContextResolver
{
    // Infrastructure subdomains (especially the Angular Identity login UI)
    // must never be interpreted as tenant workspaces by wildcard DNS routing.
    private static readonly HashSet<string> ReservedInfrastructureSlugs = new(
        ["identity", "auth", "api", "admin", "system", "www", "cdn",
         "mail", "help", "blog", "public", "support"],
        StringComparer.OrdinalIgnoreCase);

    public static bool TryResolve(
        string? requestHost,
        BffOidcSettings settings,
        out BffHostContext context)
    {
        context = new BffHostContext(string.Empty, null, string.Empty);

        if (string.IsNullOrWhiteSpace(requestHost))
            return false;

        var host = requestHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Contains(':') || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            return false;

        // Identity-only subdomains may host the same Angular assets, but may
        // NEVER issue BFF cookies, even if accidentally listed as SystemHosts.
        if (host == "identity." + settings.WorkspaceBaseDomain ||
            host == "auth." + settings.WorkspaceBaseDomain)
            return false;

        if (settings.SystemHosts.Contains(host))
        {
            context = new BffHostContext("system", null, host);
            return true;
        }

        var suffix = "." + settings.WorkspaceBaseDomain;
        if (!host.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var slug = host[..^suffix.Length];
        if (slug.Length is < 1 or > 63 ||
            slug.Contains('.') ||
            ReservedInfrastructureSlugs.Contains(slug) ||
            !IsDnsLabel(slug))
        {
            return false;
        }

        context = new BffHostContext("tenant", slug, host);
        return true;
    }

    private static bool IsDnsLabel(string value)
    {
        if (value[0] == '-' || value[^1] == '-')
            return false;

        return value.All(c =>
            char.IsAsciiLetterLower(c) ||
            char.IsAsciiDigit(c) ||
            c == '-');
    }
}
