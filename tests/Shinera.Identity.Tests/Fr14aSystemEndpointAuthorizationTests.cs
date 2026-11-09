using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;
using OpenIddict.Abstractions;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// Executable FR-14A proof: a System metadata declaration now attaches a
/// live grant gate, with deny precedence, while production defaults remain off.
/// </summary>
public sealed class Fr14aSystemEndpointAuthorizationTests
{
    private static IConfiguration Config(bool on) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:PlatformPermissions:EnforcePermissionEndpoints"] = on.ToString()
        }).Build();

    private static ClaimsPrincipal Principal(Guid id, bool tenant = false)
    {
        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, id.ToString()),
            new("account_type", tenant ? "tenant" : "system"),
            new(OpenIddictConstants.Claims.Role, "superadmin")
        };
        if (tenant) claims.Add(new Claim("tenant_id", Guid.NewGuid().ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<bool> Allowed(
        IdentityDbContext db, ClaimsPrincipal user,
        SystemFeatureEndpointPermissionRequirement requirement, bool enabled)
    {
        var context = new AuthorizationHandlerContext(
            [requirement], user, new DefaultHttpContext());
        await new SystemFeatureEndpointPermissionHandler(db, Config(enabled))
            .HandleAsync(context);
        return context.HasSucceeded;
    }

    // SQL Server generates 8-byte RowVersion in the database. The EF
    // InMemory fixture has no database-generated concurrency token; seed
    // it explicitly without changing production entity mappings.
    private static async Task SaveFixtureAsync(IdentityDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Added &&
                         entry.Metadata.FindProperty("RowVersion") != null))
            entry.Property("RowVersion").CurrentValue = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Permission_code_must_belong_to_its_registered_System_resource()
    {
        Assert.True(new SystemFeatureEndpointPermissionRequirement(
            "roles", "ROLES.READ").IsRegistered);
        Assert.True(new SystemFeatureEndpointPermissionRequirement(
            "feature-registry", "FEATURE-REGISTRY.APPLY").IsRegistered);
        Assert.False(new SystemFeatureEndpointPermissionRequirement(
            "roles", "USERS.DELETE").IsRegistered);
        Assert.False(new SystemFeatureEndpointPermissionRequirement(
            "unregistered", "UNREGISTERED.READ").IsRegistered);
    }

    [Fact]
    public async Task Enforced_System_routes_fail_closed_and_use_live_grants_and_deny()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        await db.Database.EnsureCreatedAsync();

        var masterId = DefaultSeedData.MasterTenant.Id;
        var user = new User(null, "fr14a-system",
            "fr14a@example.test", "System", "Operator", UserType.System)
        {
            IsActive = true, IsLockedOut = false
        };
        var role = new Role(masterId, "superadmin", null)
        { Scope = RoleScope.System, IsActive = true };
        var permission = new Permission(Guid.NewGuid(),
            "ROLES.READ", null, "Read roles", null);
        db.Tenants.Add(new Tenant(masterId)
        { Name = "Master", Slug = "master", IsActive = true });
        db.Users.Add(user);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.UserRoles.Add(new UserRole(masterId, user.Id, role.Id));
        await SaveFixtureAsync(db);

        var principal = Principal(user.Id);
        var requirement = new SystemFeatureEndpointPermissionRequirement(
            "roles", "ROLES.READ");
        Assert.True(await Allowed(db, principal, requirement, false));
        Assert.False(await Allowed(db, principal, requirement, true));

        // A grant comes from the live database, never a JWT permission claim.
        db.RolePermissions.Add(new RolePermission(masterId, role.Id, permission.Id));
        await SaveFixtureAsync(db);
        Assert.True(await Allowed(db, principal, requirement, true));

        db.UserPermissions.Add(new UserPermission(Guid.NewGuid())
        {
            TenantId = masterId, UserId = user.Id,
            PermissionId = permission.Id, IsGranted = false
        });
        await SaveFixtureAsync(db);
        Assert.False(await Allowed(db, principal, requirement, true));

        db.UserPermissions.Remove(db.UserPermissions.Single());
        await SaveFixtureAsync(db);
        Assert.True(await Allowed(db, principal, requirement, true));

        role.IsActive = false;
        await SaveFixtureAsync(db);
        Assert.False(await Allowed(db, principal, requirement, true));
        role.IsActive = true;
        user.IsActive = false;
        await SaveFixtureAsync(db);
        Assert.False(await Allowed(db, principal, requirement, true));
    }

    [Fact]
    public async Task Tenant_or_unknown_System_metadata_never_bypasses_the_gate()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new IdentityDbContext(options, new TenantContext());
        var user = Guid.NewGuid();
        Assert.False(await Allowed(db, Principal(user, true),
            new SystemFeatureEndpointPermissionRequirement("roles", "ROLES.READ"),
            false));
        Assert.False(await Allowed(db, Principal(user),
            new SystemFeatureEndpointPermissionRequirement("roles", "UNLISTED.ACTION"),
            false));
        Assert.False(await Allowed(db, Principal(user),
            new SystemFeatureEndpointPermissionRequirement("roles", "ROLES.READ"),
            true));
    }
}
