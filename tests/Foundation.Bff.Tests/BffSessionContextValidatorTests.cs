using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed class BffSessionContextValidatorTests
{
    [Fact]
    public void Tenant_principal_is_accepted_only_on_the_matching_callback_host()
    {
        var (settings, callback) = CreateSettings();
        var principal = TenantPrincipal("tenant-a");
        var properties = Properties("tenant", "tenant-a", callback);
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Host = new HostString("tenant-a.shinera.localhost");

        Assert.True(BffSessionContextValidator.TryValidate(
            principal, properties, request, settings, out _));

        request.Host = new HostString("tenant-b.shinera.localhost");
        Assert.False(BffSessionContextValidator.TryValidate(
            principal, properties, request, settings, out _));
    }

    [Fact]
    public void System_principal_cannot_be_used_for_a_tenant_callback()
    {
        var (settings, callback) = CreateSettings();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("account_type", "system")],
            "test"));
        var properties = Properties("system", null, callback);
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Host = new HostString("tenant-a.shinera.localhost");

        Assert.False(BffSessionContextValidator.TryValidate(
            principal, properties, request, settings, out _));
    }

    [Fact]
    public void Tenant_context_requires_matching_tenant_id_and_slug()
    {
        var (settings, callback) = CreateSettings();
        var principal = TenantPrincipal("tenant-b");
        var properties = Properties("tenant", "tenant-a", callback);
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Host = new HostString("tenant-a.shinera.localhost");

        Assert.False(BffSessionContextValidator.TryValidate(
            principal, properties, request, settings, out _));
    }

    private static ClaimsPrincipal TenantPrincipal(string slug)
        => new(new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("account_type", "tenant"),
            new Claim("tenant", slug),
            new Claim("tenant_id", Guid.NewGuid().ToString())
        ], "test"));

    private static AuthenticationProperties Properties(
        string context, string? tenant, string callback)
    {
        var properties = new AuthenticationProperties { RedirectUri = "/dashboard" };
        properties.Items[BffDefaults.LoginContextProperty] = context;
        properties.Items[BffDefaults.CallbackUriProperty] = callback;
        if (tenant is not null)
            properties.Items[BffDefaults.TenantProperty] = tenant;
        return properties;
    }

    private static (BffOidcSettings Settings, string Callback) CreateSettings()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
            ["Identity:Bff:Authority"] = "https://identity.example.com",
            ["Identity:Bff:ClientId"] = "foundation-browser-bff",
            ["Identity:Bff:ClientSecret"] = "test-secret-with-more-than-thirty-two-characters",
            ["Identity:Bff:RedirectUris:0"] = "https://tenant-a.shinera.localhost/signin-oidc"
        });
        var settings = BffOidcSettings.FromConfiguration(
            builder.Configuration, builder.Environment);
        return (settings, "https://tenant-a.shinera.localhost/signin-oidc");
    }
}
