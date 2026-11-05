using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Registers the first-party confidential browser BFF client only when explicitly
/// enabled. Client secrets and callback URIs are deployment configuration, never
/// checked into source control.
/// </summary>
public static class OpenIddictBffClientSeeder
{
    public static async Task SeedAsync(
        IOpenIddictApplicationManager applicationManager,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Identity:Bff:Enabled"))
            return;

        var clientId = configuration["Identity:Bff:ClientId"]?.Trim();
        var clientSecret = configuration["Identity:Bff:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Identity:Bff:ClientId is required when the BFF client is enabled.");

        if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Length < 32)
            throw new InvalidOperationException("Identity:Bff:ClientSecret must be configured as a high-entropy secret of at least 32 characters.");

        var redirectUris = ReadUris(configuration, "Identity:Bff:RedirectUris", environment, required: true);
        var postLogoutRedirectUris = ReadUris(configuration, "Identity:Bff:PostLogoutRedirectUris", environment, required: false);

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "Foundation Browser BFF",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddictConstants.Permissions.Prefixes.Scope + "api"
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(uri);

        foreach (var uri in postLogoutRedirectUris)
            descriptor.PostLogoutRedirectUris.Add(uri);

        var existing = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (existing is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
    }

    private static IReadOnlyList<Uri> ReadUris(
        IConfiguration configuration,
        string key,
        IHostEnvironment environment,
        bool required)
    {
        var values = configuration.GetSection(key).Get<string[]>() ?? [];
        var result = new List<Uri>();

        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new InvalidOperationException(
                    $"{key} must contain exact absolute HTTPS callback URIs (HTTP loopback is allowed only in Development).");
            }

            result.Add(uri);
        }

        if (required && result.Count == 0)
            throw new InvalidOperationException($"{key} must contain at least one exact callback URI.");

        if (result.Distinct().Count() != result.Count)
            throw new InvalidOperationException($"{key} contains duplicate callback URIs.");

        return result;
    }
}
