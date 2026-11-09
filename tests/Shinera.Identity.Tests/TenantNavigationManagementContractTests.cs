using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class TenantNavigationManagementContractTests
{
    private static IConfiguration Flags(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            { ["Identity:TenantNavigation:OwnerEditorEnabled"] = enabled.ToString() })
            .Build();

    [Fact]
    public void Requests_reject_mass_assignment_and_omitted_desired_state()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.NotNull(JsonSerializer.Deserialize<
            TenantNavigationOwnerWriter.CreateItemRequest>(
                """{"categoryId":"00000000-0000-0000-0000-000000000001","parentId":null,"title":"Home","icon":"home","route":"/dashboard","sortOrder":1}""", options));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            TenantNavigationOwnerWriter.CreateCategoryRequest>(
                """{"title":"Test","sortOrder":1,"tenantId":"forged"}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            TenantNavigationOwnerWriter.UpdateItemRequest>(
                """{"parentId":null,"title":"Home","icon":null,"route":"/dashboard","sortOrder":1,"expectedVersion":"AAA=","permissionId":"forged"}""",
                options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            TenantNavigationOwnerWriter.UpdateCategoryRequest>(
                """{"title":"Test","sortOrder":1,"expectedVersion":"AAAAAAAAAAA="}""",
                options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            TenantNavigationOwnerWriter.UpdateItemRequest>(
                """{"title":"Test","sortOrder":1,"expectedVersion":"AAAAAAAAAAA="}""",
                options));
    }

    [Fact]
    public async Task All_authoring_endpoints_are_off_by_default_and_require_https()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        var disabled = Flags(false);
        var id = Guid.NewGuid();
        Assert.IsType<StatusCodeHttpResult>(await TenantNavigationManagement.Read(
            null!, null!, http, disabled, CancellationToken.None));
        Assert.Equal(403, Assert.IsType<StatusCodeHttpResult>(
            await TenantNavigationManagement.CreateCategory(
                null!, null!, http, disabled, new("Test", 1), CancellationToken.None))
            .StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeHttpResult>(
            await TenantNavigationManagement.UpdateCategory(
                null!, null!, http, disabled, id,
                new("Test", 1, true, "AAAAAAAAAAA="), CancellationToken.None))
            .StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeHttpResult>(
            await TenantNavigationManagement.CreateItem(
                null!, null!, http, disabled,
                new(id, null, "Home", "home", "/dashboard", 1),
                CancellationToken.None)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeHttpResult>(
            await TenantNavigationManagement.UpdateItem(
                null!, null!, http, disabled, id,
                new(null, "Home", "home", "/dashboard", 1, true, "AAAAAAAAAAA="),
                CancellationToken.None)).StatusCode);

        http.Request.Scheme = "http";
        Assert.Equal(403, Assert.IsType<StatusCodeHttpResult>(
            await TenantNavigationManagement.Read(
                null!, null!, http, Flags(true), CancellationToken.None))
            .StatusCode);
    }

    [Fact]
    public void New_permissions_are_a_code_owned_page_and_action()
    {
        var read = Assert.Single(TenantWorkspacePermissionCatalog.Definitions
            .Where(x => x.Key == TenantWorkspacePermissionCatalog.NavigationRead));
        var manage = Assert.Single(TenantWorkspacePermissionCatalog.Definitions
            .Where(x => x.Key == TenantWorkspacePermissionCatalog.NavigationManage));
        Assert.Equal("PAGE", read.Kind);
        Assert.Equal("ACTION", manage.Kind);
        Assert.Equal("/dashboard/navigation", read.Route);
        Assert.Equal(read.Route, manage.Route);
    }
}
