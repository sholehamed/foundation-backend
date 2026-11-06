using System.Text.Json;
using Modules.System.Identity.Application.Features.Menus.Queries;
using Modules.System.Identity.Application.Features.MenuCategories.Queries;
using Xunit;

public sealed class MenuReadContractTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Menu_get_by_id_exposes_canonical_fields_and_legacy_read_aliases()
    {
        var parent = Guid.NewGuid();
        var dto = new MenuGetByIdDto
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ParentId = parent,
            Route = "/system/users",
            ExternalUrl = null,
            Title = "Users",
            Icon = "group",
            Order = 2,
            IsActive = true
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, ApiJson));
        var root = json.RootElement;

        Assert.Equal(parent.ToString(), root.GetProperty("parentId").GetString());
        Assert.Equal(parent.ToString(), root.GetProperty("pMenuId").GetString());
        Assert.Equal("/system/users", root.GetProperty("route").GetString());
        Assert.Equal("/system/users", root.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("externalUrl").ValueKind);
    }

    [Fact]
    public void Paged_list_serializes_canonical_menu_contract_without_dropping_legacy_aliases()
    {
        var parentId = Guid.NewGuid();
        var dto = new MenuPagedListDto
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ParentId = parentId,
            ParentTitle = "Administration",
            Route = "/system/roles",
            Title = "Roles",
            ExternalUrl = null
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, ApiJson));
        var root = json.RootElement;

        Assert.Equal(parentId.ToString(), root.GetProperty("parentId").GetString());
        Assert.Equal(parentId.ToString(), root.GetProperty("pMenuId").GetString());
        Assert.Equal("Administration", root.GetProperty("parentTitle").GetString());
        Assert.Equal("Administration", root.GetProperty("pMenu").GetString());
        Assert.Equal("/system/roles", root.GetProperty("route").GetString());
        Assert.Equal("/system/roles", root.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("externalUrl").ValueKind);
    }

    [Fact]
    public void Category_menu_row_has_external_url_for_editing_external_menu_items()
    {
        var dto = new MenuCategoryGetMenusDto
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Title = "Docs",
            ExternalUrl = "https://docs.example.test",
            Route = null,
            IsActive = true
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, ApiJson));
        Assert.Equal("https://docs.example.test",
            json.RootElement.GetProperty("externalUrl").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("route").ValueKind);
    }
}
