using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Abstractions
{
    public interface IIdentityDbContext:IBaseDbContext
    {
        DbSet<GroupRole> GroupRoles { get; }
        DbSet<Group> Groups { get; }
        DbSet<Module> Modules { get; }
        DbSet<PermissionApiResource> PermissionApiResources { get; }
        DbSet<Permission> Permissions { get; }
        DbSet<PermissionUiResource> PermissionUiResources { get; }
        DbSet<Resource> Resources { get; }
        DbSet<RolePermission> RolePermissions { get; }
        DbSet<Role> Roles { get; }
        DbSet<Tenant> Tenants { get; }
        DbSet<BusinessProfile> BusinessProfiles { get; }
        DbSet<Branch> Branches { get; }
        DbSet<UserGroup> UserGroups { get; }
        DbSet<UserPermission> UserPermissions { get; }
        DbSet<UserRole> UserRoles { get; }
        DbSet<User> Users { get; }
        DbSet<Menu> Menus { get; }
        DbSet<MenuCategory> MenuCategories { get; }
        DbSet<ApiResource> ApiResources { get; }
        DbSet<UiResource> UiResources { get; }
        DbSet<TenantModule> TenantModules { get; }
        DbSet<UserTenantAccess> UserTenantAccess { get; }
        DbSet<TenantClosure> TenantClosure { get; }
    }
}
