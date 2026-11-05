using Domain.SharedKernel.Common;
using Infrastructure.SharedKernel.Persistence.Contexts;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using System.Reflection;

namespace Modules.System.Identity.Infrastructure.Persistence.Contexts
{
    

    public class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext) : BaseDbContext(options), IIdentityDbContext
    {
        public DbSet<Group> Groups => Set<Group>();
        public DbSet<GroupRole> GroupRoles => Set<GroupRole>();
        public DbSet<Domain.Entities.Module> Modules => Set<Domain.Entities.Module>();
        public DbSet<PermissionApiResource> PermissionApiResources => Set<PermissionApiResource>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<Resource> Resources => Set<Resource>();
        public DbSet<TenantModule> TenantModules => Set<TenantModule>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<User> Users => Set<User>();
        public DbSet<UserGroup> UserGroups => Set<UserGroup>();
        public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<PermissionUiResource> PermissionUiResources => Set<PermissionUiResource>();
        public DbSet<Menu> Menus=> Set<Menu>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<ApiResource> ApiResources => Set<ApiResource>();
        public DbSet<UiResource> UiResources => Set<UiResource>();

        public DbSet<UserTenantAccess> UserTenantAccess => Set<UserTenantAccess>();

        public DbSet<TenantClosure> TenantClosure => Set<TenantClosure>();



        // ارجاع به اعضای نمونه، تا EF آن‌ها را پارامتر کند
        private bool FilterDisabled => tenantContext.IsFilterDisabled;
        private Guid[] ReadScope => tenantContext.ReadScope;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.UseOpenIddict();
            var assembly = Assembly.GetExecutingAssembly();
            builder.ApplyConfigurationsFromAssembly(assembly);
            base.OnModelCreating(builder);
            foreach(var entityType in builder.Model.GetEntityTypes())
        {
                if (entityType.BaseType is not null) continue;               // TPH: فقط ریشه
                if (entityType.IsOwned()) continue;
                if (!typeof(IMustHaveTenant).IsAssignableFrom(entityType.ClrType)) continue;

                typeof(IdentityDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [builder]);
            }
        }


        private void ApplyTenantFilter<TEntity>(ModelBuilder builder)
            where TEntity : class, IMustHaveTenant
        {
            builder.Entity<TEntity>().HasQueryFilter("tenant",e =>
                FilterDisabled || ReadScope.Contains(e.TenantId));
        }
    }

}
