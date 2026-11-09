using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class Fr14bSystemFirstLoginHostTests
{
    private static IConfiguration Config(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Identity:PlatformAccounts:FirstLoginEnabled"] = enabled.ToString(),
                ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost"
            }).Build();

    private static DefaultHttpContext Request(
        string hostname, bool https = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Host = new HostString(hostname);
        return context;
    }

    [Fact]
    public void System_first_login_is_default_off_HTTPS_and_root_origin_only()
    {
        Assert.False(Auth.SystemFirstLoginHostAllowed(
            Request("shinera.localhost"), Config(false)));
        Assert.False(Auth.SystemFirstLoginHostAllowed(
            Request("shinera.localhost", https: false), Config(true)));
        Assert.True(Auth.SystemFirstLoginHostAllowed(
            Request("shinera.localhost"), Config(true)));
        Assert.False(Auth.SystemFirstLoginHostAllowed(
            Request("tenant.shinera.localhost"), Config(true)));
        Assert.False(Auth.SystemFirstLoginHostAllowed(
            Request("identity.shinera.localhost"), Config(true)));
        Assert.False(Auth.SystemFirstLoginHostAllowed(
            Request("evil-shinera.localhost"), Config(true)));
    }

    [Fact]
    public void Proof_contract_rejects_injected_tenant_or_caller_selected_account()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var valid = JsonSerializer.Deserialize<SystemFirstLoginWriter.StartRequest>(
            """{"userName":"pending","temporaryPassword":"AAAAAA-AAAAAA-AAAAAA-AAAAAA"}""",
            options);
        Assert.NotNull(valid);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemFirstLoginWriter.StartRequest>(
                """{"userName":"pending","temporaryPassword":"AAAAAA-AAAAAA-AAAAAA-AAAAAA","tenantId":"forged"}""",
                options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemFirstLoginWriter.CompleteRequest>(
                """{"challenge":"abc","newPassword":"some-value","userId":"forged"}""",
                options));
    }

    [Fact]
    public void System_secret_shape_has_24_generated_characters_and_dashes()
    {
        Assert.True(SystemFirstLoginWriter.LooksLikeIssuedPassword(
            "ABCDEF-GHJKLM-NPQRST-UVWXYZ"));
        Assert.False(SystemFirstLoginWriter.LooksLikeIssuedPassword(
            "ordinary-password"));
        Assert.False(SystemFirstLoginWriter.LooksLikeIssuedPassword(
            "ABCDEF-GHJKLM-NPQRST-UVWXYZ-extra"));
    }
}
