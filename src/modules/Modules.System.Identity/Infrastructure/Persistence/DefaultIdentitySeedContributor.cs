using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Real startup seed orchestration. Never seed the historical sample menus,
/// bogus route strings or sample tenants from the removed DefaultSeedData lists.
/// FeatureRegistryDefinitions, TenantWorkspacePermissionCatalog and
/// TenantWorkspaceNavigationService own those code-defined catalogs.
/// </summary>
public sealed class DefaultIdentitySeedContributor : ISeedContributor
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<IdentityDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();

        await SeedOpenIddictAsync(applications);
        await OpenIddictBffClientSeeder.SeedAsync(applications, configuration, environment);

        // The stable Master storage scope is always required; new catalog
        // definitions and Master-role permissions are added ONLY to a fresh DB.
        // On restarts or preexisting installations do not revive revoked grants.
        var master = await EnsureTenantAsync(db, DefaultSeedData.CreateMasterTenant());
        if (master.Id != DefaultSeedData.MasterTenantId)
            throw new InvalidOperationException(
                "Master Tenant id does not match the stable first-install scope.");

        await InitialAccessBootstrap.SeedFreshInstallAsync(db);
        // Historical platform keys still need additive definition-only support
        // for existing databases. This never assigns a Role/UserPermission.
        await PlatformPermissionCatalogSeeder.SeedDefinitionsAsync(db);

        if (!environment.IsDevelopment())
            return;

        // Preserve the current local Development experience. The fallback is
        // NEVER used outside Development; set the environment variable on any
        // shared developer host and never copy it to Production.
        var password = Environment.GetEnvironmentVariable(
            "SHINERA_DEVELOPMENT_ADMIN_PASSWORD") ?? "123456";
        await EnsureUserAsync(db, hasher,
            DefaultSeedData.CreateDevelopmentAdmin(), password);
        await EnsureDevelopmentSystemAdminRoleAsync(db, master.Id);

        if (configuration.GetValue<bool>(
                "Identity:PlatformPermissions:BootstrapDevelopmentGrants"))
            await PlatformPermissionCatalogSeeder.GrantDevelopmentSuperAdminAsync(db);

        if (configuration.GetValue<bool>("Identity:E2E:Enabled"))
            await SeedDisposableE2EAsync(db, hasher, password, master.Id);
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


    // Existing Development System administrator assignments are kept exactly
    // as before. Never create/upgrade/mutate Production System accounts here.
    private static async Task EnsureDevelopmentSystemAdminRoleAsync(
        IdentityDbContext db, Guid masterId)
    {
        var role = await db.Roles.IgnoreQueryFilters(["tenant"])
            .SingleOrDefaultAsync(x => x.TenantId == masterId &&
                x.NormalizedName == "SUPERADMIN");
        if (role is null)
        {
            role = new Role(masterId, DefaultSeedData.SuperAdminRoleName,
                "Local development system administrator")
            {
                Scope = RoleScope.System
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var admin = await db.Users.SingleOrDefaultAsync(x =>
            x.UserType == UserType.System && x.TenantId == null &&
            x.NormalizedUserName == "ADMIN");
        if (admin is null) return;

        if (!await db.UserRoles.IgnoreQueryFilters(["tenant"]).AnyAsync(x =>
                x.UserId == admin.Id && x.RoleId == role.Id &&
                x.TenantId == masterId))
        {
            db.UserRoles.Add(new UserRole(masterId, admin.Id, role.Id));
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Tenant> EnsureTenantAsync(
        IdentityDbContext db, Tenant seed)
    {
        var existing = await db.Tenants.SingleOrDefaultAsync(t =>
            t.Slug == seed.Slug);
        if (existing is not null) return existing;
        db.Tenants.Add(seed);
        await db.SaveChangesAsync();
        return seed;
    }

    private static async Task EnsureUserAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        User seed, string password)
    {
        var username = seed.UserName.Trim().ToUpperInvariant();
        var exists = await db.Users.AnyAsync(x =>
            x.TenantId == seed.TenantId &&
            x.NormalizedUserName == username);
        if (exists) return;

        seed.NormalizedUserName = username;
        seed.NormalizedEmail = seed.Email?.Trim().ToUpperInvariant();
        seed.EmailConfirmed = true;
        seed.IsActive = true;
        seed.IsLockedOut = false;
        seed.AccessFailedCount = 0;
        seed.SecurityStamp = Guid.NewGuid().ToString("N");
        seed.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        seed.PasswordHash = hasher.HashPassword(seed, password);
        db.Users.Add(seed);
        await db.SaveChangesAsync();
    }

    private static async Task SeedDisposableE2EAsync(
        IdentityDbContext db, IPasswordHasher<User> hasher,
        string password, Guid masterId)
    {
        // Explicit Development/CI-only fixture. It is NEVER an initial
        // Production Tenant, and E2E users receive no System role grants.
        await EnsureUserAsync(db, hasher,
            new User(null, "e2e-viewer", "e2e-viewer@localhost.test",
                "E2E", "Viewer", UserType.System), password);

        var tenant = await EnsureTenantAsync(db,
            new Tenant(null, "E2E HTTP Tenant", "e2e-http-ci", "localhost"));
        await EnsureUserAsync(db, hasher,
            new User(tenant.Id, "e2e-tenant", "e2e-tenant@localhost.test",
                "E2E", "Tenant", UserType.Tenant), password);

        var groupId = Guid.Parse("7fbf4183-c8ab-4e2e-805d-65fd7fa95247");
        if (!await db.Groups.IgnoreQueryFilters(["tenant"])
                .AnyAsync(g => g.Id == groupId))
        {
            db.Groups.Add(new Group(groupId)
            {
                TenantId = masterId,
                Name = "E2E HTTP Operators",
                NormalizedName = "E2E HTTP OPERATORS",
                IsActive = true
            });
        }

        var permissionId = Guid.Parse("45129362-d8b8-4bde-8600-98745e311923");
        if (!await db.Permissions.AnyAsync(p => p.Id == permissionId))
            db.Permissions.Add(new Permission(permissionId,
                "e2e.http-access", null, "E2E HTTP access", null));

        const string code = "e2e-http-permissions";
        var moduleId = Guid.Parse("bc3fd038-e283-46a0-92d3-0eb916c54491");
        var resourceId = Guid.Parse("d051bd62-6828-421c-a0bf-70ca4ed29ae3");
        if (!await db.Modules.AnyAsync(m => m.Id == moduleId))
            db.Modules.Add(new Module(moduleId, code,
                "E2E Permission Module", null, 0, true));
        await db.SaveChangesAsync();
        if (!await db.Resources.AnyAsync(x => x.Id == resourceId))
            db.Resources.Add(new Resource(resourceId, moduleId, code,
                "E2E Permission Resource", null, 0, true));
        await db.SaveChangesAsync();
    }
}
