using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Coarse, fail-closed platform administration boundary. ADR-003 resource/action
/// permissions remain a separate future authorization layer.
/// </summary>
public static class SystemAuthorizationPolicies
{
    public const string Administrator = "SystemAdministrator";

    public static bool IsSystemAdministrator(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true
           && user.HasClaim("account_type", "system")
           && !user.HasClaim(c => c.Type == "tenant_id")
           && user.FindAll(OpenIddictConstants.Claims.Role).Any(role =>
               string.Equals(role.Value, "superadmin", StringComparison.OrdinalIgnoreCase));
}
