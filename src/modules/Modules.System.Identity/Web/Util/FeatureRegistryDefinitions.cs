using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

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
             "ADMINS.CHANGE-STATE", "ADMINS.CHANGE-LOCK", "ADMINS.ASSIGN-ROLE", "ADMINS.ASSIGN-PERMISSION"]),
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
        new("system", "System", "feature-registry", "Feature Registry",
            "system.feature-registry", "رجیستری قابلیت‌ها", "/system/feature-registry", "sync", 120,
            "FEATURE-REGISTRY.READ",
            ["FEATURE-REGISTRY.READ", "FEATURE-REGISTRY.APPLY"])
    ];

    /// <summary>
    /// The backend is the authority for stable feature keys and required
    /// view Permission.Code. Angular owns executable routes only and verifies
    /// their paths against this contract before offering catalog Apply.
    /// The DTO contains no user grants or mutable database configuration.
    /// </summary>
    public sealed record Manifest(int SchemaVersion, IReadOnlyList<ManifestFeature> Features);

    public sealed record ManifestFeature(
        string Key,
        string ModuleCode,
        string ResourceCode,
        string Route,
        string PageKey,
        string ReadPermission);

    public static Manifest ExportManifest() => new(1, Features
        .Select(x => new ManifestFeature(x.PageKey, x.ModuleCode,
            x.ResourceCode, x.Route, x.PageKey, x.ReadPermission))
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
        endpoint.WithMetadata(new FeatureEndpointMetadata(
            key.ToUpperInvariant(), resourceCode.ToLowerInvariant(),
            permissionCode.ToUpperInvariant(), title));
        return endpoint;
    }
}
