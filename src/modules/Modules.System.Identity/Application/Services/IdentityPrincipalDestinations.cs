using System.Security.Claims;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Application.Services;

/// <summary>
/// Applies the same minimal ID-token/access-token claim policy at both the
/// authorization and authorization-code exchange stages.
/// </summary>
public static class IdentityPrincipalDestinations
{
    public static void Apply(
        ClaimsPrincipal principal,
        IReadOnlyCollection<string> scopes)
    {
        var profileRequested = scopes.Contains(OpenIddictConstants.Scopes.Profile);
        var emailRequested = scopes.Contains(OpenIddictConstants.Scopes.Email);

        foreach (var claim in principal.Claims)
        {
            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Subject:
                    claim.SetDestinations(
                        OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken);
                    break;

                case OpenIddictConstants.Claims.Username:
                    claim.SetDestinations(profileRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case OpenIddictConstants.Claims.Email:
                    claim.SetDestinations(emailRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case OpenIddictConstants.Claims.GivenName:
                case OpenIddictConstants.Claims.Picture:
                    claim.SetDestinations(profileRequested
                        ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                        : [OpenIddictConstants.Destinations.AccessToken]);
                    break;

                case "auth_time":
                    claim.SetDestinations(OpenIddictConstants.Destinations.IdentityToken);
                    break;

                case OpenIddictConstants.Claims.Role:
                case "tenant_id":
                case "tenant":
                case "permission":
                case "permission_code":
                case "account_type":
                    claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
                    break;
            }
        }
    }
}
