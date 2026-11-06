using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Modules.System.Bff;

/// <summary>
/// Validated deployment settings for the confidential first-party OIDC client.
/// The secret is intentionally never included in ToString()/logs.
/// </summary>
public sealed class BffOidcSettings
{
    private BffOidcSettings(
        Uri authority,
        string clientId,
        string clientSecret,
        string workspaceBaseDomain,
        IReadOnlySet<string> systemHosts,
        IReadOnlySet<string> redirectUris)
    {
        Authority = authority;
        ClientId = clientId;
        ClientSecret = clientSecret;
        WorkspaceBaseDomain = workspaceBaseDomain;
        SystemHosts = systemHosts;
        RedirectUris = redirectUris;
    }

    public Uri Authority { get; }
    public string ClientId { get; }
    internal string ClientSecret { get; }
    public string WorkspaceBaseDomain { get; }
    public IReadOnlySet<string> SystemHosts { get; }
    public IReadOnlySet<string> RedirectUris { get; }

    public bool IsRegisteredRedirectUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return RedirectUris.Contains(uri.AbsoluteUri);
    }

    public static BffOidcSettings FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var authorityValue = configuration["Identity:Bff:Authority"];
        if (!Uri.TryCreate(authorityValue, UriKind.Absolute, out var authority) ||
            !IsAllowedScheme(authority, environment) ||
            !string.IsNullOrEmpty(authority.UserInfo) ||
            !string.IsNullOrEmpty(authority.Query) ||
            !string.IsNullOrEmpty(authority.Fragment) ||
            authority.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "Identity:Bff:Authority must be an absolute HTTPS authority (HTTP loopback is allowed only in Development).");
        }

        var clientId = configuration["Identity:Bff:ClientId"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Identity:Bff:ClientId is required when the BFF is enabled.");

        var secret = configuration["Identity:Bff:ClientSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException(
                "Identity:Bff:ClientSecret must be a high-entropy secret of at least 32 characters from a secret store.");

        var baseDomain = configuration["Tenant:WorkspaceBaseDomain"]?
            .Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseDomain) ||
            Uri.CheckHostName(baseDomain) == UriHostNameType.Unknown ||
            baseDomain.Contains(':') || baseDomain.Contains('/'))
        {
            throw new InvalidOperationException(
                "Tenant:WorkspaceBaseDomain must be configured before enabling the BFF.");
        }

        var redirectValues = configuration.GetSection("Identity:Bff:RedirectUris").Get<string[]>() ?? [];
        var redirects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in redirectValues)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var redirect) ||
                !IsAllowedScheme(redirect, environment) ||
                !string.IsNullOrEmpty(redirect.UserInfo) ||
                !string.IsNullOrEmpty(redirect.Query) ||
                !string.IsNullOrEmpty(redirect.Fragment) ||
                redirect.AbsolutePath != BffDefaults.CallbackPath)
            {
                throw new InvalidOperationException(
                    "Identity:Bff:RedirectUris must contain exact absolute callback URIs ending in /signin-oidc.");
            }

            if (!redirects.Add(redirect.AbsoluteUri))
                throw new InvalidOperationException("Identity:Bff:RedirectUris contains duplicate callback URIs.");
        }

        if (redirects.Count == 0)
            throw new InvalidOperationException(
                "Identity:Bff:RedirectUris must contain at least one exact callback URI.");

        var systemHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            baseDomain
        };
        if (environment.IsDevelopment())
            systemHosts.Add("localhost");

        foreach (var host in configuration.GetSection("Identity:Bff:SystemHosts").Get<string[]>() ?? [])
        {
            var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.Contains(':') ||
                Uri.CheckHostName(normalized) == UriHostNameType.Unknown)
            {
                throw new InvalidOperationException(
                    "Identity:Bff:SystemHosts must contain host names only (without ports or schemes).");
            }

            systemHosts.Add(normalized);
        }

        return new BffOidcSettings(
            authority,
            clientId,
            secret,
            baseDomain,
            systemHosts,
            redirects);
    }

    private static bool IsAllowedScheme(Uri uri, IHostEnvironment environment)
        => uri.Scheme == Uri.UriSchemeHttps ||
           (environment.IsDevelopment() &&
            uri.Scheme == Uri.UriSchemeHttp &&
            uri.IsLoopback);
}
