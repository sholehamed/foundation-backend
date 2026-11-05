using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Infrastructure.Persistence;

public sealed class DefaultIdentitySeedContributor : ISeedContributor
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher<User>>();
        var applicationManager = serviceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
        await SeedOpenIddictAsync(applicationManager);
        await OpenIddictBffClientSeeder.SeedAsync(applicationManager, configuration, environment);
        var masterTenant = await EnsureTenantAsync(db, DefaultSeedData.MasterTenant);
        // Additive, idempotent definitions. No privileges are granted in Production.
        await PlatformPermissionCatalogSeeder.SeedDefinitionsAsync(db);

        // Never create an account with a known development password in production.
        if (environment.IsDevelopment())
        {
            var developmentPassword = Environment.GetEnvironmentVariable("SHINERA_DEVELOPMENT_ADMIN_PASSWORD")
                ?? "123456"; // Legacy local-only default; override via environment variable.
            await EnsureUserAsync(db, passwordHasher, DefaultSeedData.Users.ToArray(), developmentPassword);
            await EnsureDevelopmentSystemAdminRoleAsync(db, masterTenant.Id);
            // Explicit Development/CI opt-in only. Never silently restore grants in Production.
            if (configuration.GetValue<bool>("Identity:PlatformPermissions:BootstrapDevelopmentGrants"))
                await PlatformPermissionCatalogSeeder.GrantDevelopmentSuperAdminAsync(db);
            if (configuration.GetValue<bool>("Identity:E2E:Enabled"))
            {
                // Disposable Development/CI HTTP fixtures ONLY. They deliberately
                // hold no SuperAdmin grants and must never be seeded in Production.
                await EnsureUserAsync(db, passwordHasher,
                    [new User(null, "e2e-viewer", "e2e-viewer@localhost.test",
                        "E2E", "Viewer", UserType.System)], developmentPassword);

                var e2eTenant = await EnsureTenantAsync(db,
                    new Tenant(null, "E2E HTTP Tenant", "e2e-http-ci", "localhost"));
                await EnsureUserAsync(db, passwordHasher,
                    [new User(e2eTenant.Id, "e2e-tenant",
                        "e2e-tenant@localhost.test", "E2E", "Tenant", UserType.Tenant)],
                    developmentPassword);

                var groupId = Guid.Parse("7fbf4183-c8ab-4e2e-805d-65fd7fa95247");
                if (!await db.Groups.IgnoreQueryFilters(["tenant"])
                        .AnyAsync(g => g.Id == groupId))
                {
                    db.Groups.Add(new Group(groupId)
                    {
                        TenantId = masterTenant.Id,
                        Name = "E2E HTTP Operators",
                        NormalizedName = "E2E HTTP OPERATORS",
                        IsActive = true
                    });
                }

                var permissionId = Guid.Parse("45129362-d8b8-4bde-8600-98745e311923");
                if (!await db.Permissions.AnyAsync(p => p.Id == permissionId))
                {
                    db.Permissions.Add(new Permission(permissionId,
                        "e2e.http-access", null, "E2E HTTP access", null));
                }
                // Explicit E2E-only Resource fixture for validating real Permission
                // Create/Update FK behavior. No global/prod resource seed changes.
                var e2eModuleId = Guid.Parse("bc3fd038-e283-46a0-92d3-0eb916c54491");
                var e2eResourceId = Guid.Parse("d051bd62-6828-421c-a0bf-70ca4ed29ae3");
                if (!await db.Modules.AnyAsync(m => m.Id == e2eModuleId))
                {
                    db.Modules.Add(new Module(e2eModuleId,
                        "e2e-http-permissions", "E2E Permission Module", null, 0, true));
                }
                await db.SaveChangesAsync();
                if (!await db.Resources.AnyAsync(r => r.Id == e2eResourceId))
                {
                    db.Resources.Add(new Resource(e2eResourceId, e2eModuleId,
                        "e2e-http-permissions", "E2E Permission Resource", null, 0, true));
                }
                await db.SaveChangesAsync();
            }
        }
        //await EnsureTenantsAsync(db, DefaultSeedData.Tenants);


        //await EnsureModulesAsync(db, DefaultSeedData.Modules);
        //await EnsureResourcesAsync(db, DefaultSeedData.Resources);
        //await EnsureUiAsync(db, DefaultSeedData.UiResources.ToArray());
        //await EnsureApiAsync(db, DefaultSeedData.ApiResources.ToArray());
        //await EnsureTenantModulesAsync(db, DefaultSeedData.TenantModules);
        //await EnsurePermissionsAsync(db, DefaultSeedData.Permissions);
        //await EnsureUserRoleAsync(db, DefaultSeedData.UserRoles.ToArray());
        //await EnsureRoleAsync(db, DefaultSeedData.Roles.ToArray());
        //await EnsureRolePermissionsAsync(db, DefaultSeedData.RolePermissions.ToArray());
        //await EnsureMenuAsync(db, DefaultSeedData.MenuCategories.ToArray());
    }

    private static async Task SeedOpenIddictAsync(IOpenIddictApplicationManager applicationManager)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "postman",
            ClientType = "public",
            ConsentType = "explicit",
            DisplayName = "Test Client Application",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.GrantTypes.Password,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Prefixes.Scope + "api"
            }
        };

        var exists = await applicationManager.FindByClientIdAsync(descriptor.ClientId);
        if (exists is null)
        {
            await applicationManager.CreateAsync(descriptor);
        }
        else
        {
            // This client may predate the introduction of the revocation endpoint.
            // Preserve all existing secrets, grants, redirects and custom settings
            // instead of overwriting it with the default seed descriptor.
            var existingDescriptor = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(existingDescriptor, exists);

            if (existingDescriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation))
            {
                await applicationManager.UpdateAsync(exists, existingDescriptor);
            }
        }
    }

    // Only called in Development. Existing production installations need an
    // explicit, reviewed migration of their platform administrator assignments.
    private static async Task EnsureDevelopmentSystemAdminRoleAsync(IdentityDbContext db, Guid systemScopeTenantId)
    {
        var role = await db.Roles.IgnoreQueryFilters(["tenant"])
            .FirstOrDefaultAsync(x => x.TenantId == systemScopeTenantId && x.NormalizedName == "SUPERADMIN");

        if (role is null)
        {
            role = new Role(systemScopeTenantId, "superadmin", "Local development system administrator");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var admin = await db.Users.FirstOrDefaultAsync(x =>
            x.UserType == UserType.System && x.TenantId == null && x.NormalizedUserName == "ADMIN");

        if (admin is null)
            return;

        var assigned = await db.UserRoles.IgnoreQueryFilters(["tenant"])
            .AnyAsync(x => x.UserId == admin.Id && x.RoleId == role.Id && x.TenantId == systemScopeTenantId);

        if (!assigned)
        {
            db.UserRoles.Add(new UserRole(systemScopeTenantId, admin.Id, role.Id));
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Tenant> EnsureTenantAsync(IdentityDbContext db, Tenant seed)
    {

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Slug == seed.Slug);
        if (tenant is not null)
            return tenant;



        db.Tenants.Add(seed);
        await db.SaveChangesAsync();
        return seed;
    }
    private static async Task EnsureApiResourceAsync(IdentityDbContext db, UiResource[] uiResources)
    {
        foreach (var seed in uiResources)
        {
            db.UiResources.Add(seed);
            await db.SaveChangesAsync();
        }
        return;
    }
    private static async Task EnsureApiResourceAsync(IdentityDbContext db, ApiResource[] apiResources)
    {
        foreach (var seed in apiResources)
        {
            db.ApiResources.Add(seed);
            await db.SaveChangesAsync();
        }
        return;
    }
    private static async Task EnsureMenuAsync(IdentityDbContext db, MenuCategory[] menuCategories)
    {
        foreach (var seed in menuCategories)
        {
            db.MenuCategories.Add(seed);
            await db.SaveChangesAsync();
        }
        return;
    }
    private static async Task EnsureRolePermissionsAsync(IdentityDbContext db, RolePermission[] rolePermissions)
    {
        foreach (var seed in rolePermissions)
        {

            var tenant = await db.RolePermissions.FirstOrDefaultAsync(x => x.TenantId == seed.TenantId && x.PermissionId == seed.PermissionId && x.RoleId == seed.RoleId);
            if (tenant is not null)
                return;
            db.RolePermissions.Add(seed);
            await db.SaveChangesAsync();
        }
        return;
    }
    private static async Task EnsureUserRoleAsync(IdentityDbContext db, UserRole[] userroles)
    {
        foreach (var seed in userroles)
        {

            var tenant = await db.UserRoles.FirstOrDefaultAsync(x => x.TenantId == seed.TenantId && x.UserId == seed.UserId && x.RoleId == seed.RoleId);
            if (tenant is not null)
                return;
            db.UserRoles.Add(seed);
            await db.SaveChangesAsync();
        }
        return;
    }
    private static async Task EnsureApiAsync(IdentityDbContext db, ApiResource[] apiResources)
    {
        foreach (var seed in apiResources)
        {
            var tenant = await db.ApiResources.FirstOrDefaultAsync(x => x.Key == seed.Key);
            if (tenant is not null)
                return;
            db.ApiResources.Add(seed);
        }
        await db.SaveChangesAsync();
        return;
    }
    private static async Task EnsureUiAsync(IdentityDbContext db, UiResource[] uiResources)
    {
        foreach (var seed in uiResources)
        {
            var tenant = await db.UiResources.FirstOrDefaultAsync(x => x.Key == seed.Key);
            if (tenant is not null)
                return;
            db.UiResources.Add(seed);
        }
        await db.SaveChangesAsync();
        return;
    }
    private static async Task EnsureRoleAsync(IdentityDbContext db, Role[] roles)
    {
        foreach (var seed in roles)
        {
            var tenant = await db.Roles.FirstOrDefaultAsync(x => x.NormalizedName == seed.NormalizedName);
            if (tenant is not null)
                return;
            db.Roles.Add(seed);
        }
        await db.SaveChangesAsync();
        return;
    }
    public static async Task EnsureModulesAsync(
       IdentityDbContext db,
       IReadOnlyCollection<Module> seeds)
    {

        foreach (var seed in seeds)
        {
            bool exists = await db.Modules.AnyAsync(x => x.Code == seed.Code);
            if (!exists)
            {
                await db.Modules.AddAsync(seed);
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task EnsurePermissionsAsync(
    IdentityDbContext db,
    IReadOnlyCollection<Permission> seeds)
    {

        foreach (var seed in seeds)
        {
            bool exists = await db.Permissions.AnyAsync(x => x.ResourceId == seed.ResourceId && x.Code == seed.Code);
            if (!exists)
            {
                await db.Permissions.AddAsync(seed);
            }
        }
        await db.SaveChangesAsync();
    }
    public static async Task EnsureTenantModulesAsync(
     IdentityDbContext db,
     IReadOnlyCollection<TenantModule> seeds)
    {

        foreach (var seed in seeds)
        {
            bool exists = await db.TenantModules.AnyAsync(x => x.ModuleId == seed.ModuleId && x.TenantId == seed.TenantId);
            if (!exists)
            {
                await db.TenantModules.AddAsync(seed);
            }
        }
        await db.SaveChangesAsync();
    }
    public static async Task EnsureResourcesAsync(
     IdentityDbContext db,
     IReadOnlyCollection<Resource> seeds)
    {

        foreach (var seed in seeds)
        {
            bool exists = await db.Resources.AnyAsync(x => x.Code == seed.Code);
            if (!exists)
            {
                await db.Resources.AddAsync(seed);
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task EnsureTenantsAsync(
       IdentityDbContext db,
       IReadOnlyCollection<Tenant> seeds)
    {

        foreach (var seed in seeds)
        {

            bool exists = await db.Tenants.AnyAsync(x => x.Slug == seed.Slug);
            if (!exists)
            {
                await db.Tenants.AddAsync(seed);
            }
        }
        await db.SaveChangesAsync();
    }







    private static async Task EnsureUserAsync(
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        User[] users, string password)
    {
        var t = DefaultSeedData.Tenants;
        foreach (var seed in users)
        {

            var normalizedUserName = seed.UserName.Trim().ToUpperInvariant();
            var normalizedEmail = seed.Email.Trim().ToUpperInvariant();

            var user = await db.Users.FirstOrDefaultAsync(x =>
                x.TenantId == seed.TenantId && x.NormalizedUserName == normalizedUserName);

            if (user is not null)
                continue;


            seed.TenantId = seed.TenantId;
            seed.UserName = seed.UserName;
            seed.NormalizedUserName = normalizedUserName;
            seed.Email = seed.Email;
            seed.NormalizedEmail = normalizedEmail;
            seed.EmailConfirmed = true;
            seed.FirstName = seed.FirstName;
            seed.LastName = seed.LastName;
            seed.IsActive = true;
            seed.IsLockedOut = false;
            seed.AccessFailedCount = 0;
            seed.SecurityStamp = Guid.NewGuid().ToString("N");
            seed.ConcurrencyStamp = Guid.NewGuid().ToString("N");


            seed.PasswordHash = passwordHasher.HashPassword(seed, password);

            db.Users.Add(seed);
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserRoleAsync(IdentityDbContext db, Guid tenantId, Guid userId, Guid roleId)
    {
        var exists = await db.UserRoles.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.RoleId == roleId);

        if (!exists)
        {
            db.UserRoles.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId });
        }
    }

    private static async Task EnsureUserGroupAsync(IdentityDbContext db, Guid tenantId, Guid userId, Guid groupId)
    {
        var exists = await db.UserGroups.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.GroupId == groupId);

        if (!exists)
        {
            db.UserGroups.Add(new UserGroup { TenantId = tenantId, UserId = userId, GroupId = groupId });
        }
    }

    private static async Task EnsureRolePermissionAsync(IdentityDbContext db, Guid tenantId, Guid roleId, Guid permissionId)
    {
        var exists = await db.RolePermissions.AnyAsync(x =>
            x.TenantId == tenantId && x.RoleId == roleId && x.PermissionId == permissionId);

        if (!exists)
        {
            db.RolePermissions.Add(new RolePermission
            {
                TenantId = tenantId,
                RoleId = roleId,
                PermissionId = permissionId
            });
        }
    }

   

    private static async Task EnsureUserPermissionAsync(IdentityDbContext db, Guid tenantId, Guid userId, Guid permissionId)
    {
        var exists = await db.UserPermissions.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.PermissionId == permissionId);

        if (!exists)
        {
            db.UserPermissions.Add(new UserPermission
            {
                TenantId = tenantId,
                UserId = userId,
                PermissionId = permissionId
            });
        }
    }



    private static async Task<Menu> EnsureMenuAsync(
        IdentityDbContext db,
        Guid categoryId,
        string? parentTitle,
        string title,
        string icon,
        short order,
        string route,
        Guid? permissionId)
    {
        var menu = await db.Menus.FirstOrDefaultAsync(x =>
            x.CategoryId == categoryId && x.Title == title);

        if (menu is not null)
            return menu;

        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(parentTitle))
        {
            parentId = await db.Menus
                .Where(x => x.CategoryId == categoryId && x.Title == parentTitle)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();
        }

        menu = new Menu
        {
            CategoryId = categoryId,
            ParentId = parentId,
            Title = title,
            Icon = icon,
            Order = order,
            Route = route,
            PermissionId = permissionId,
            IsActive = true,
            IsHidden = false
        };

        db.Menus.Add(menu);
        return menu;
    }
}
