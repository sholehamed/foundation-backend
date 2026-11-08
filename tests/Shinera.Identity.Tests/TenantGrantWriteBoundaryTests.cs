using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Xunit;

public sealed class TenantGrantWriteBoundaryTests
{
    [Fact]
    public async Task Generic_tenant_SaveChanges_cannot_grant_roles_permissions_or_group_rights()
    {
        var (db, current, _) = OpenTenant();
        await using (db)
        {
            var tenant = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            var role = Guid.NewGuid();
            var member = Guid.NewGuid();
            var perm = Guid.NewGuid();

            db.UserRoles.Add(new UserRole(tenant, member, role));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.RolePermissions.Add(new RolePermission(tenant, role, perm));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.UserPermissions.Add(new UserPermission
            {
                TenantId = tenant, UserId = actor, PermissionId = perm,
                IsGranted = true,
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.GroupRoles.Add(new GroupRole
            {
                TenantId = tenant, GroupId = Guid.NewGuid(), RoleId = role
            });
            Assert.Throws<UnauthorizedAccessException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();

            db.UserGroups.Add(new UserGroup
            {
                TenantId = tenant, UserId = member, GroupId = Guid.NewGuid()
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Pair_scoped_intent_allows_only_reviewed_exact_target_and_expires()
    {
        var (db, current, trusted) = OpenTenant();
        await using (db)
        {
            var tenant = current.ActiveTenantId!.Value;
            var actor = current.UserId!.Value;
            var member = Guid.NewGuid();
            var role = Guid.NewGuid();
            var other = Guid.NewGuid();
            var permission = Guid.NewGuid();

            Assert.Throws<UnauthorizedAccessException>(() =>
                trusted.BeginTenantGrantMutation(current, actor, "UserRole", [role]));
            Assert.Throws<UnauthorizedAccessException>(() =>
                trusted.BeginTenantGrantMutation(current, member, "UserRole", [Guid.Empty]));

            using (trusted.BeginTenantGrantMutation(current, member, "UserRole", [role]))
            {
                db.UserRoles.Add(new UserRole(tenant, other, role));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                db.UserRoles.Add(new UserRole(tenant, member, role) { RowVersion = [1] });
                await db.SaveChangesAsync();
            }

            db.ChangeTracker.Clear();
            db.UserRoles.Add(new UserRole(tenant, member, other));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            using (trusted.BeginTenantGrantMutation(current, role, "RolePermission", [permission]))
            {
                db.RolePermissions.Add(new RolePermission(tenant, other, permission));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => db.SaveChangesAsync());
                db.ChangeTracker.Clear();
                db.RolePermissions.Add(new RolePermission(tenant, role, permission) { RowVersion = [1] });
                await db.SaveChangesAsync();
            }

            db.ChangeTracker.Clear();
            db.RolePermissions.Add(new RolePermission(tenant, role, other));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Signup_owner_can_be_created_once_with_new_tenant_user_and_role()
    {
        var (db, current, _) = OpenTenant();
        await using (db)
        {
            var tenantId = current.ActiveTenantId!.Value;
            var actorId = current.UserId!.Value;
            var tenant = new Tenant(tenantId, null, "Salon", "new-tenant", "salon.test")
            {
                RowVersion = [1], IsActive = true,
            };
            var user = new User(actorId, tenantId, "owner", "owner@salon.test",
                "Tenant", "Owner", UserType.Tenant)
            {
                RowVersion = [1], IsActive = true, NormalizedUserName = "OWNER",
                NormalizedEmail = "OWNER@SALON.TEST", PasswordHash = "fixture",
            };
            var owner = new Role(tenantId, "Owner", "Provisioned")
            {
                RowVersion = [1], Scope = RoleScope.Tenant,
            };
            db.Tenants.Add(tenant);
            db.Users.Add(user);
            db.Roles.Add(owner);
            db.UserRoles.Add(new UserRole(tenantId, user.Id, owner.Id) { RowVersion = [1] });
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            // Owner is now existing: no direct second link can masquerade
            // as a fresh, un-audited signup.
            db.UserRoles.Add(new UserRole(tenantId, Guid.NewGuid(), owner.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Trusted_tenant_scopes_cannot_be_opened_from_system_context_or_nested()
    {
        var (db, current, trusted) = OpenTenant();
        await using (db)
        {
            var member = Guid.NewGuid();
            var role = Guid.NewGuid();
            using (trusted.BeginTenantGrantMutation(current, member, "UserRole", [role]))
            {
                Assert.Throws<UnauthorizedAccessException>(() =>
                    trusted.BeginTenantGrantMutation(current, member, "UserRole", [role]));
            }

            var system = new TenantContext();
            system.InitializeSystem(Guid.NewGuid(), isSuperAdmin: true);
            Assert.Throws<UnauthorizedAccessException>(() =>
                trusted.BeginTenantGrantMutation(system, member, "UserRole", [role]));

            // After scope disposal, no one can mutate an ordinary tenant
            // grant through generic SaveChanges.
            db.UserRoles.Add(new UserRole(current.ActiveTenantId!.Value, member, role));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        }
    }

    private static (IdentityDbContext Db, TenantContext Current,
        TrustedTenantWriteScope Trusted) OpenTenant()
    {
        var current = new TenantContext();
        var tenantId = Guid.NewGuid();
        current.InitializeTenant(Guid.NewGuid(), tenantId,
            [tenantId], [tenantId], tenantId);
        var trusted = new TrustedTenantWriteScope(
            NullLogger<TrustedTenantWriteScope>.Instance);
        var guard = new TenantSaveChangesInterceptor(current, trusted);
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(guard).Options;
        return (new IdentityDbContext(options, current), current, trusted);
    }
}
