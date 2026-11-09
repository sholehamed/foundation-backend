using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// FR-13B-3C: only a verified dedicated Identity origin may complete
/// the restricted first-login exchange. No Tenant, System or arbitrary
/// Host header may reuse the IdP endpoint.
/// </summary>
public sealed class InteractiveFirstLoginHostTests
{
    private static IConfiguration Settings(bool enabled = true, string? authority = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Identity:TenantFirstLogin:Enabled"] = enabled ? "true" : "false",
            ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
            ["Identity:Bff:Authority"] = authority,
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpContext Http(string host, bool https = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Host = new HostString(host);
        return context;
    }

    [Theory]
    [InlineData("identity.shinera.localhost", true)]
    [InlineData("auth.shinera.localhost", true)]
    [InlineData("tenant-a.shinera.localhost", false)]
    [InlineData("shinera.localhost", false)]
    [InlineData("system.shinera.localhost", false)]
    [InlineData("identity.attacker.test", false)]
    [InlineData("evil.identity.shinera.localhost", false)]
    public void Only_reserved_identity_hosts_can_complete_first_login(
        string hostname, bool allowed)
    {
        Assert.Equal(allowed, Auth.FirstLoginIdentityHostAllowed(
            Http(hostname), Settings()));
    }

    [Fact]
    public void Configuration_is_fail_closed_on_disabled_or_insecure_requests()
    {
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost"), Settings(enabled: false)));
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost", https: false), Settings()));
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost:8080"),
            Settings(authority: "https://identity.shinera.localhost")));
    }

    [Fact]
    public void Explicit_authority_pins_identity_host_and_port_exactly()
    {
        Assert.True(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost"),
            Settings(authority: "https://identity.shinera.localhost")));
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("auth.shinera.localhost"),
            Settings(authority: "https://identity.shinera.localhost")));
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost"),
            Settings(authority: "https://identity.shinera.localhost/login")));
        Assert.False(Auth.FirstLoginIdentityHostAllowed(
            Http("identity.shinera.localhost"),
            Settings(authority: "http://identity.shinera.localhost")));
    }
}
