using System.Security.Claims;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

public sealed class SystemAuthorizationPoliciesTests
{
    private static ClaimsPrincipal Principal(
        string? accountType = "system",
        string? role = "superadmin",
        bool authenticated = true,
        bool tenantClaim = false)
    {
        var claims = new List<Claim>();
        if (accountType is not null)
            claims.Add(new Claim("account_type", accountType));
        if (role is not null)
            claims.Add(new Claim(OpenIddictConstants.Claims.Role, role));
        if (tenantClaim)
            claims.Add(new Claim("tenant_id", Guid.NewGuid().ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
    }

    [Fact]
    public void Authenticated_platform_superadmin_is_authorized()
        => Assert.True(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(role: "SuperAdmin")));

    [Fact]
    public void An_authenticated_system_user_without_admin_role_is_denied()
        => Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(role: null)));

    [Fact]
    public void Tenant_superadmin_is_not_platform_admin()
        => Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(accountType: "tenant")));

    [Fact]
    public void System_user_with_tenant_id_is_denied()
        => Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(tenantClaim: true)));

    [Fact]
    public void Anonymous_caller_is_denied()
        => Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(authenticated: false)));

    [Fact]
    public void Missing_or_unknown_account_type_is_denied()
    {
        Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(accountType: null)));
        Assert.False(SystemAuthorizationPolicies.IsSystemAdministrator(Principal(accountType: "other")));
    }
}
