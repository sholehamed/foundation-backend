using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Code-owned stable feature identifiers. Only declarations in this allowlist
/// can be reconciled into the Identity catalog. A route/title change does not
/// change Permission.Code, UI.Key or API.Key.
/// </summary>
public static class FeatureRegistryDefinitions
{
    public sealed record Feature(
        string ModuleCode,
        string ModuleTitle,
        string ResourceCode,
        string ResourceTitle,
        string PageKey,
        string PageTitle,
        string Route,
        string Icon,
        short Order,
        string ReadPermission,
        string[] PermissionCodes);

    public const string NavigationCategory = "مدیریت سیستم";

    public static readonly IReadOnlyList<Feature> Features =
    [
        new("system", "System", "roles", "Roles",
            "system.roles", "نقش‌ها", "/system/roles", "shield_person", 30,
            "ROLES.READ",
            ["ROLES.READ", "ROLES.CREATE", "ROLES.UPDATE", "ROLES.DELETE",
             "ROLES.CHANGE-STATE", "ROLES.ASSIGN-PERMISSIONS"]),
        new("system", "System", "permissions", "Permissions",
            "system.permissions", "مجوزها", "/system/permissions", "verified_user", 40,
            "PERMISSIONS.READ",
            ["PERMISSIONS.READ", "PERMISSIONS.CREATE", "PERMISSIONS.UPDATE",
             "PERMISSIONS.DELETE", "PERMISSIONS.CHANGE-STATE"]),
        new("system", "System", "users", "Users",
            "system.users", "کاربران", "/system/users", "group", 20,
            "USERS.READ",
            ["USERS.READ", "USERS.CREATE", "USERS.UPDATE", "USERS.DELETE",
             "USERS.CHANGE-STATE", "USERS.CHANGE-LOCK",
             "USERS.ASSIGN-ROLE", "USERS.ASSIGN-PERMISSION"]),
        new("system", "System", "admins", "Admins",
            "system.admins", "مدیران سیستم", "/system/admins", "admin_panel_settings", 10,
            "ADMINS.READ",
            ["ADMINS.READ", "ADMINS.CREATE", "ADMINS.UPDATE", "ADMINS.DELETE",
             "ADMINS.CHANGE-STATE", "ADMINS.CHANGE-LOCK", "ADMINS.ASSIGN-ROLE", "ADMINS.ASSIGN-PERMISSION",
             "ADMINS.ISSUE-TEMP-PASSWORD"]),
        new("system", "System", "groups", "Groups",
            "system.groups", "گروه‌های کاربری", "/system/groups", "groups", 50,
            "GROUPS.READ",
            ["GROUPS.READ", "GROUPS.CREATE", "GROUPS.UPDATE", "GROUPS.DELETE",
             "GROUPS.CHANGE-STATE", "GROUPS.MANAGE-MEMBERS", "GROUPS.ASSIGN-ROLE"]),
        new("system", "System", "modules", "Modules",
            "system.modules", "ماژول‌ها", "/system/modules", "view_module", 60,
            "MODULES.READ",
            ["MODULES.READ", "MODULES.CREATE", "MODULES.UPDATE", "MODULES.DELETE",
             "MODULES.CHANGE-STATE"]),
        new("system", "System", "tenants", "Tenants",
            "system.tenants", "مستأجرها", "/system/tenants", "apartment", 70,
            "TENANTS.READ",
            ["TENANTS.READ", "TENANTS.CREATE", "TENANTS.UPDATE", "TENANTS.DELETE",
             "TENANTS.CHANGE-STATE"]),
        new("system", "System", "resources", "Resources",
            "system.resources", "منابع", "/system/resources", "category", 80,
            "RESOURCES.READ",
            ["RESOURCES.READ", "RESOURCES.CREATE", "RESOURCES.UPDATE", "RESOURCES.DELETE",
             "RESOURCES.CHANGE-STATE"]),
        new("system", "System", "api-resources", "API Resources",
            "system.api-resources", "منابع سرویس‌ها", "/system/api-resources", "api", 90,
            "API-RESOURCES.READ",
            ["API-RESOURCES.READ", "API-RESOURCES.CREATE", "API-RESOURCES.UPDATE", "API-RESOURCES.DELETE",
             "API-RESOURCES.CHANGE-STATE", "API-RESOURCES.SYNC"]),
        new("system", "System", "ui-resources", "UI Resources",
            "system.ui-resources", "منابع صفحات", "/system/ui-resources", "web", 100,
            "UI-RESOURCES.READ",
            ["UI-RESOURCES.READ", "UI-RESOURCES.CREATE", "UI-RESOURCES.UPDATE", "UI-RESOURCES.DELETE",
             "UI-RESOURCES.CHANGE-STATE"]),
        new("system", "System", "menu-management", "Menu Management",
            "system.menu-management", "مدیریت منو", "/system/menu-management", "menu", 110,
            "MENU-MANAGEMENT.READ",
            ["MENU-MANAGEMENT.READ", "MENU-MANAGEMENT.CREATE", "MENU-MANAGEMENT.UPDATE", "MENU-MANAGEMENT.DELETE",
             "MENU-MANAGEMENT.REORDER"]),
        new("system", "System", "dashboard", "Dashboard",
            "system.dashboard", "داشبورد سیستم", "/system/dashboard", "dashboard", 0,
            "DASHBOARD.READ",
            ["DASHBOARD.READ"]),
        new("system", "System", "plan-catalog", "Plan Catalog",
            "system.plan-catalog", "پلن‌ها و اشتراک‌ها", "/system/plan-catalog", "sell", 125,
            "PLAN-CATALOG.READ",
            ["PLAN-CATALOG.READ", "PLAN-CATALOG.CREATE",
             "PLAN-CATALOG.UPDATE", "PLAN-CATALOG.PUBLISH", "PLAN-CATALOG.SETTLE",
             "PLAN-CATALOG.COUPONS"]),
        new("system", "System", "control-tower", "Control Tower",
            "system.control-tower", "مرکز پایش", "/system/control-tower", "monitor_heart", 130,
            "CONTROL-TOWER.TRACES.READ",
            ["CONTROL-TOWER.TRACES.READ"]),
        new("system", "System", "feature-registry", "Feature Registry",
            "system.feature-registry", "رجیستری قابلیت‌ها", "/system/feature-registry", "sync", 120,
            "FEATURE-REGISTRY.READ",
            ["FEATURE-REGISTRY.READ", "FEATURE-REGISTRY.APPLY"])
    ];

    /// <summary>
    /// Reviewable subordinate UI declarations. Routes are Angular URL
    /// templates, not independently navigable Menu entries. ACTION entries
    /// are metadata only and do not imply frontend button enforcement.
    /// Inactive-by-default System Admin/User forms remain non-live pending
    /// an independent safe identity write workflow.
    /// </summary>
    public sealed record UiElement(
        string FeatureKey,
        string Key,
        string Title,
        string? Route,
        UiResourceType Type,
        string PermissionCode,
        bool DefaultActive);

    public static readonly IReadOnlyList<UiElement> UiElements =
    [
        new("system.admins", "system.admins.create", "ایجاد مدیر",
            "/system/admins/create", UiResourceType.PAGE, "ADMINS.CREATE", false),
        new("system.admins", "system.admins.edit", "ویرایش مدیر",
            "/system/admins/:userId/edit", UiResourceType.PAGE, "ADMINS.UPDATE", false),
        new("system.users", "system.users.create", "ایجاد کاربر",
            "/system/users/create", UiResourceType.PAGE, "USERS.CREATE", false),
        new("system.users", "system.users.edit", "ویرایش کاربر",
            "/system/users/:userId/edit", UiResourceType.PAGE, "USERS.UPDATE", false),
        new("system.roles", "system.roles.create", "ایجاد نقش",
            "/system/roles/create", UiResourceType.PAGE, "ROLES.CREATE", true),
        new("system.roles", "system.roles.edit", "ویرایش نقش",
            "/system/roles/:roleId/edit", UiResourceType.PAGE, "ROLES.UPDATE", true),
        new("system.roles", "system.roles.permissions", "مدیریت مجوزهای نقش",
            "/system/roles/:roleId/permissions", UiResourceType.PAGE, "ROLES.ASSIGN-PERMISSIONS", true),
        new("system.groups", "system.groups.create", "ایجاد گروه",
            "/system/groups/create", UiResourceType.PAGE, "GROUPS.CREATE", true),
        new("system.groups", "system.groups.edit", "ویرایش گروه",
            "/system/groups/:groupId/edit", UiResourceType.PAGE, "GROUPS.UPDATE", true),
        new("system.groups", "system.groups.members", "مدیریت اعضای گروه",
            "/system/groups/:groupId/members", UiResourceType.PAGE, "GROUPS.MANAGE-MEMBERS", true),
        new("system.modules", "system.modules.create", "ایجاد ماژول",
            "/system/modules/create", UiResourceType.PAGE, "MODULES.CREATE", true),
        new("system.modules", "system.modules.edit", "ویرایش ماژول",
            "/system/modules/:moduleId/edit", UiResourceType.PAGE, "MODULES.UPDATE", true),
        new("system.tenants", "system.tenants.create", "ایجاد مستأجر",
            "/system/tenants/create", UiResourceType.PAGE, "TENANTS.CREATE", true),
        new("system.tenants", "system.tenants.edit", "ویرایش مستأجر",
            "/system/tenants/:tenantId/edit", UiResourceType.PAGE, "TENANTS.UPDATE", true),
        new("system.tenants", "system.tenants.overview", "نمای کلی مستأجر",
            "/system/tenants/:tenantId/overview", UiResourceType.PAGE, "TENANTS.READ", true),
        new("system.resources", "system.resources.create", "ایجاد منبع",
            "/system/resources/create", UiResourceType.PAGE, "RESOURCES.CREATE", true),
        new("system.resources", "system.resources.edit", "ویرایش منبع",
            "/system/resources/:resourceId/edit", UiResourceType.PAGE, "RESOURCES.UPDATE", true),
        new("system.permissions", "system.permissions.resource-list", "مجوزهای منبع",
            "/system/resources/:resourceId/permissions", UiResourceType.PAGE, "PERMISSIONS.READ", true),
        new("system.api-resources", "system.api-resources.resource-list", "سرویس‌های منبع",
            "/system/resources/:resourceId/apiResources", UiResourceType.PAGE, "API-RESOURCES.READ", true),
        new("system.ui-resources", "system.ui-resources.resource-list", "صفحات منبع",
            "/system/resources/:resourceId/uiResources", UiResourceType.PAGE, "UI-RESOURCES.READ", true),
        new("system.api-resources", "system.api-resources.create", "ایجاد منبع API",
            "/system/api-resources/create", UiResourceType.PAGE, "API-RESOURCES.CREATE", true),
        new("system.api-resources", "system.api-resources.edit", "ویرایش منبع API",
            "/system/api-resources/:apiResourceId/edit", UiResourceType.PAGE, "API-RESOURCES.UPDATE", true),
        new("system.ui-resources", "system.ui-resources.create", "ایجاد منبع UI",
            "/system/ui-resources/create", UiResourceType.PAGE, "UI-RESOURCES.CREATE", true),
        new("system.ui-resources", "system.ui-resources.edit", "ویرایش منبع UI",
            "/system/ui-resources/:uiResourceId/edit", UiResourceType.PAGE, "UI-RESOURCES.UPDATE", true),
        new("system.permissions", "system.permissions.create", "ایجاد مجوز",
            "/system/permissions/create", UiResourceType.PAGE, "PERMISSIONS.CREATE", true),
        new("system.permissions", "system.permissions.edit", "ویرایش مجوز",
            "/system/permissions/:permissionId/edit", UiResourceType.PAGE, "PERMISSIONS.UPDATE", true),
        new("system.permissions", "system.permissions.resource-create", "ایجاد مجوز منبع",
            "/system/resources/:resourceId/permissions/create", UiResourceType.PAGE, "PERMISSIONS.CREATE", true),
        new("system.permissions", "system.permissions.resource-edit", "ویرایش مجوز منبع",
            "/system/resources/:resourceId/permissions/:permissionId/edit", UiResourceType.PAGE, "PERMISSIONS.UPDATE", true),
        new("system.api-resources", "system.api-resources.resource-create", "ایجاد سرویس منبع",
            "/system/resources/:resourceId/apiResources/create", UiResourceType.PAGE, "API-RESOURCES.CREATE", true),
        new("system.api-resources", "system.api-resources.resource-edit", "ویرایش سرویس منبع",
            "/system/resources/:resourceId/apiResources/:apiResourceId/edit", UiResourceType.PAGE, "API-RESOURCES.UPDATE", true),
        new("system.ui-resources", "system.ui-resources.resource-create", "ایجاد صفحه منبع",
            "/system/resources/:resourceId/uiResources/create", UiResourceType.PAGE, "UI-RESOURCES.CREATE", true),
        new("system.ui-resources", "system.ui-resources.resource-edit", "ویرایش صفحه منبع",
            "/system/resources/:resourceId/uiResources/:uiResourceId/edit", UiResourceType.PAGE, "UI-RESOURCES.UPDATE", true),
        new("system.roles", "system.roles.action.assign-permissions", "تخصیص مجوزهای نقش",
            null, UiResourceType.ACTION, "ROLES.ASSIGN-PERMISSIONS", true),
        new("system.groups", "system.groups.action.assign-role", "تخصیص نقش گروه",
            null, UiResourceType.ACTION, "GROUPS.ASSIGN-ROLE", true),
        new("system.admins", "system.admins.action.issue-temp-password", "صدور رمز اولیه مدیر",
            null, UiResourceType.ACTION, "ADMINS.ISSUE-TEMP-PASSWORD", false),
        new("system.admins", "system.admins.action.assign-role", "تخصیص نقش مدیر",
            null, UiResourceType.ACTION, "ADMINS.ASSIGN-ROLE", false),
        new("system.users", "system.users.action.assign-role", "تخصیص نقش کاربر",
            null, UiResourceType.ACTION, "USERS.ASSIGN-ROLE", false),
        new("system.menu-management", "system.menu-management.action.create", "ساخت منو",
            null, UiResourceType.ACTION, "MENU-MANAGEMENT.CREATE", true),
        new("system.menu-management", "system.menu-management.action.update", "ویرایش منو",
            null, UiResourceType.ACTION, "MENU-MANAGEMENT.UPDATE", true),
        new("system.menu-management", "system.menu-management.action.delete", "حذف منو",
            null, UiResourceType.ACTION, "MENU-MANAGEMENT.DELETE", true),
        new("system.menu-management", "system.menu-management.action.reorder", "مرتب‌سازی منو",
            null, UiResourceType.ACTION, "MENU-MANAGEMENT.REORDER", true),
        new("system.feature-registry", "system.feature-registry.action.apply", "اعمال رجیستری",
            null, UiResourceType.ACTION, "FEATURE-REGISTRY.APPLY", true),
        new("system.roles", "system.roles.action.delete", "حذف",
            null, UiResourceType.ACTION, "ROLES.DELETE", true),
        new("system.roles", "system.roles.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "ROLES.CHANGE-STATE", true),
        new("system.permissions", "system.permissions.action.delete", "حذف",
            null, UiResourceType.ACTION, "PERMISSIONS.DELETE", true),
        new("system.permissions", "system.permissions.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "PERMISSIONS.CHANGE-STATE", true),
        new("system.users", "system.users.action.delete", "حذف",
            null, UiResourceType.ACTION, "USERS.DELETE", false),
        new("system.users", "system.users.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "USERS.CHANGE-STATE", false),
        new("system.admins", "system.admins.action.delete", "حذف",
            null, UiResourceType.ACTION, "ADMINS.DELETE", false),
        new("system.admins", "system.admins.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "ADMINS.CHANGE-STATE", false),
        new("system.groups", "system.groups.action.delete", "حذف",
            null, UiResourceType.ACTION, "GROUPS.DELETE", true),
        new("system.groups", "system.groups.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "GROUPS.CHANGE-STATE", true),
        new("system.modules", "system.modules.action.delete", "حذف",
            null, UiResourceType.ACTION, "MODULES.DELETE", true),
        new("system.modules", "system.modules.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "MODULES.CHANGE-STATE", true),
        new("system.tenants", "system.tenants.action.delete", "حذف",
            null, UiResourceType.ACTION, "TENANTS.DELETE", true),
        new("system.tenants", "system.tenants.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "TENANTS.CHANGE-STATE", true),
        new("system.resources", "system.resources.action.delete", "حذف",
            null, UiResourceType.ACTION, "RESOURCES.DELETE", true),
        new("system.resources", "system.resources.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "RESOURCES.CHANGE-STATE", true),
        new("system.api-resources", "system.api-resources.action.delete", "حذف",
            null, UiResourceType.ACTION, "API-RESOURCES.DELETE", true),
        new("system.api-resources", "system.api-resources.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "API-RESOURCES.CHANGE-STATE", true),
        new("system.ui-resources", "system.ui-resources.action.delete", "حذف",
            null, UiResourceType.ACTION, "UI-RESOURCES.DELETE", true),
        new("system.ui-resources", "system.ui-resources.action.change-state", "تغییر وضعیت",
            null, UiResourceType.ACTION, "UI-RESOURCES.CHANGE-STATE", true),
        new("system.admins", "system.admins.action.change-lock", "قفل و بازکردن مدیر",
            null, UiResourceType.ACTION, "ADMINS.CHANGE-LOCK", false),
        new("system.users", "system.users.action.change-lock", "قفل و بازکردن کاربر",
            null, UiResourceType.ACTION, "USERS.CHANGE-LOCK", false),
        new("system.admins", "system.admins.action.assign-permission", "تخصیص مجوز مستقیم مدیر",
            null, UiResourceType.ACTION, "ADMINS.ASSIGN-PERMISSION", false),
        new("system.users", "system.users.action.assign-permission", "تخصیص مجوز مستقیم کاربر",
            null, UiResourceType.ACTION, "USERS.ASSIGN-PERMISSION", false),
        new("system.groups", "system.groups.action.manage-members", "افزودن یا حذف عضو گروه",
            null, UiResourceType.ACTION, "GROUPS.MANAGE-MEMBERS", true),
        new("system.api-resources", "system.api-resources.action.sync", "همگام‌سازی منابع API",
            null, UiResourceType.ACTION, "API-RESOURCES.SYNC", true)
    ];

    /// <summary>
    /// The backend is the authority for stable feature keys and required
    /// view Permission.Code. Angular owns executable routes only and verifies
    /// their paths against this contract before offering catalog Apply.
    /// The DTO contains no user grants or mutable database configuration.
    /// </summary>
    public sealed record Manifest(
        int SchemaVersion,
        IReadOnlyList<ManifestFeature> Features,
        IReadOnlyList<ManifestUiElement> UiElements);

    public sealed record ManifestUiElement(
        string FeatureKey,
        string Key,
        string? Route,
        string Type,
        string PermissionCode,
        bool DefaultActive);

    public sealed record ManifestFeature(
        string Key,
        string ModuleCode,
        string ResourceCode,
        string Route,
        string PageKey,
        string ReadPermission);

    public static Manifest ExportManifest() => new(2, Features
        .Select(x => new ManifestFeature(x.PageKey, x.ModuleCode,
            x.ResourceCode, x.Route, x.PageKey, x.ReadPermission))
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .ToArray(),
        UiElements.Select(x => new ManifestUiElement(
            x.FeatureKey, x.Key, x.Route, x.Type.ToString(),
            x.PermissionCode, x.DefaultActive))
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .ToArray());
}

/// <summary>
/// Explicit endpoint descriptor, installed independently from enforcement
/// feature flags. No permission is inferred from URL/handler method names.
/// </summary>
public sealed record FeatureEndpointMetadata(
    string Key,
    string ResourceCode,
    string PermissionCode,
    string Title);

public static class FeatureEndpointMetadataExtensions
{
    public static TBuilder WithFeatureEndpoint<TBuilder>(
        this TBuilder endpoint,
        string key,
        string resourceCode,
        string permissionCode,
        string title)
        where TBuilder : IEndpointConventionBuilder
    {
        var resource = resourceCode.ToLowerInvariant();
        var permission = permissionCode.ToUpperInvariant();
        endpoint.WithMetadata(new FeatureEndpointMetadata(
            key.ToUpperInvariant(), resource, permission, title));
        // Descriptive metadata alone is NOT API authorization. Register
        // a live permission requirement on every mapped System feature:
        // its handler is strict when the reviewed rollout flag is ON.
        // The existing group-level SystemAdministrator policy remains.
        endpoint.RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new SystemFeatureEndpointPermissionRequirement(resource, permission))
            .Build());
        return endpoint;
    }
}
