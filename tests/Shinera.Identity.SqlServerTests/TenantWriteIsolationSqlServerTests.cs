using System.Security.Claims;
using OpenIddict.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Application.Features.Roles.Commands;
using Modules.System.Identity.Application.Features.Groups.Commands;
using OpenIddict.Abstractions;
using System.Security.Claims;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

public sealed class TenantWriteIsolationSqlServerTests
{
    [Fact]
    public async Task Tenant_write_scope_allows_own_rows_and_rejects_cross_tenant_insert_update_delete()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var a = new Tenant(null, "Tenant A", "write-a", "localhost");
            var b = new Tenant(null, "Tenant B", "write-b", "localhost");
            setup.AddRange(a, b);
            var roleB = new Role(b.Id, "foreign-role", "Must not be writable");
            setup.Roles.Add(roleB);
            await setup.SaveChangesAsync();

            tenant.InitializeTenant(Guid.NewGuid(), a.Id, [a.Id], [a.Id], a.Id);

            // Added records with no TenantId inherit the active authorized Tenant.
            var own = new Role(Guid.Empty, "own", "Allowed");
            guard.Roles.Add(own);
            await guard.SaveChangesAsync();
            Assert.Equal(a.Id, own.TenantId);

            // A read filter bypass does not grant a write bypass.
            using (tenant.DisableFilter())
            {
                guard.Roles.Add(new Role(b.Id, "foreign-added", null));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            }
            guard.ChangeTracker.Clear();

            var foreign = await guard.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.Id == roleB.Id);
            foreign.Description = "Attempted cross-tenant modification";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            guard.Roles.Remove(await guard.Roles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.Id == roleB.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            // The synchronous SaveChanges path must receive the same guard.
            guard.Roles.Add(new Role(b.Id, "cross-sync", null));
            Assert.Throws<UnauthorizedAccessException>(() => guard.SaveChanges());
            guard.ChangeTracker.Clear();
        });
    }

    [Fact]
    public async Task Tenant_id_reassignment_is_denied_even_with_both_tenants_in_write_scope()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, _) =>
        {
            var a = new Tenant(null, "Tenant A", "reassign-a", "localhost");
            var b = new Tenant(null, "Tenant B", "reassign-b", "localhost");
            var role = new Role(a.Id, "can-not-move", null);
            setup.AddRange(a, b, role);
            await setup.SaveChangesAsync();

            tenant.InitializeTenant(Guid.NewGuid(), a.Id, [a.Id, b.Id], [a.Id, b.Id], a.Id);
            var tracked = await guard.Roles.SingleAsync(x => x.Id == role.Id);
            tracked.TenantId = b.Id;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();
        });
    }

    [Fact]
    public async Task Anonymous_and_unscoped_system_writes_fail_but_explicit_master_scope_is_bounded()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var master = new Tenant(DefaultSeedData.MasterTenant.Id, null,
                "Platform", "platform-write-test", "localhost");
            var foreign = new Tenant(null, "Salon", "salon-write-test", "localhost");
            setup.AddRange(master, foreign);
            await setup.SaveChangesAsync();

            guard.Roles.Add(new Role(master.Id, "anonymous-forbidden", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            var actor = Guid.NewGuid();
            tenant.InitializeSystem(actor, isSuperAdmin: true);

            guard.Roles.Add(new Role(master.Id, "implicit-system-forbidden", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            Assert.Throws<UnauthorizedAccessException>(() =>
                trusted.BeginPlatformMasterWrite(tenant, Guid.NewGuid(), master.Id));
            Assert.Throws<UnauthorizedAccessException>(() =>
                trusted.BeginPlatformMasterWrite(tenant, actor, foreign.Id));

            using (trusted.BeginPlatformMasterWrite(tenant, actor, master.Id))
            {
                guard.Roles.Add(new Role(master.Id, "platform-allowed", null));
                await guard.SaveChangesAsync();

                guard.Roles.Add(new Role(foreign.Id, "platform-cross-tenant-forbidden", null));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            }
            guard.ChangeTracker.Clear();

            guard.Roles.Add(new Role(master.Id, "expired-platform-scope", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();
        });
    }

    [Fact]
    public async Task Bootstrap_scope_is_explicit_and_does_not_leak_after_disposal()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var foreign = new Tenant(null, "Bootstrap tenant", "bootstrap-tenant", "localhost");
            setup.Tenants.Add(foreign);
            await setup.SaveChangesAsync();

            using (trusted.BeginBootstrapWrites())
            {
                guard.Roles.Add(new Role(foreign.Id, "seeded-role", null));
                await guard.SaveChangesAsync();
            }
            guard.ChangeTracker.Clear();

            guard.Roles.Add(new Role(foreign.Id, "outside-bootstrap", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task Platform_role_writer_allows_master_role_but_rejects_foreign_scope_and_revoked_actor()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guarded, trusted) =>
        {
            var scope = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(scope, null, "Platform", "write-master", "localhost");
            var foreign = new Tenant(null, "Other salon", "write-foreign", "localhost");
            var actor = new User(null, "role-admin", "role-admin@example.test",
                "Platform", "Admin", UserType.System)
            {
                NormalizedUserName = "ROLE-ADMIN",
                NormalizedEmail = "ROLE-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only",
                SecurityStamp = "test-stamp"
            };
            var superAdmin = new Role(scope, "superadmin", "Privileged System user");
            var membership = new UserRole(scope, actor.Id, superAdmin.Id);
            var foreignRole = new Role(foreign.Id, "foreign-role", null);
            setup.AddRange(master, foreign, actor, superAdmin, membership, foreignRole);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var writer = new PlatformRoleWriteExecutor(
                new AuthorizedPlatformMasterWrite(guarded, tenant, trusted,
                    NullLogger<AuthorizedPlatformMasterWrite>.Instance),
                guarded, trusted, NullLogger<PlatformRoleWriteExecutor>.Instance);

            var id = await writer.ExecuteAsync(principal, "roles.create", async () =>
            {
                var created = new Role(scope, "Platform editor", "Test role");
                guarded.Roles.Add(created);
                await guarded.SaveChangesAsync();
                return created.Id;
            });
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(scope, (await guarded.Roles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().SingleAsync(x => x.Id == id)).TenantId);
            Assert.False(trusted.AllowsPlatformTenant(scope));

            // Trusted reads are not a privilege to mutate another tenant.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.update", () => Task.CompletedTask, foreignRole.Id));

            // A generic administrator Role CRUD endpoint may not forge the
            // name that is used for the live platform authorization decision.
            var mutationRan = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.create", () =>
                {
                    mutationRan = true;
                    return Task.CompletedTask;
                }, newRoleName: " superADMIN "));
            Assert.False(mutationRan);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.update", () => Task.CompletedTask,
                id, newRoleName: "SUPERADMIN"));

            // Deleting, deactivating or replacing the grants of the existing
            // SuperAdmin role is a reviewed migration, not ordinary CRUD.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.delete", () => Task.CompletedTask, superAdmin.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.permissions.replace", () => Task.CompletedTask, superAdmin.Id));
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(scope));

            // Revoked role membership invalidates a previously issued access token.
            setup.UserRoles.Remove(membership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.create", () => Task.CompletedTask));
            Assert.False(trusted.AllowsPlatformTenant(scope));
        });
    }

    [Fact]
    public async Task Scoped_role_permission_assignment_is_atomic_and_cannot_target_foreign_role()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guarded, trusted) =>
        {
            var scope = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(scope, null, "Platform", "permissions-master", "localhost");
            var foreign = new Tenant(null, "Other salon", "permissions-foreign", "localhost");
            var actor = new User(null, "permission-admin", "permission-admin@example.test",
                "Platform", "Admin", UserType.System)
            {
                NormalizedUserName = "PERMISSION-ADMIN",
                NormalizedEmail = "PERMISSION-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only",
                SecurityStamp = "test-stamp"
            };
            var adminRole = new Role(scope, "superadmin", "Admin");
            var target = new Role(scope, "operator", "Managed");
            var otherRole = new Role(foreign.Id, "foreign", "Denied");
            var membership = new UserRole(scope, actor.Id, adminRole.Id);
            var permission = new Permission { Code = "ROLES.VIEW", Name = "roles.view" };
            var unrelatedPermission = new Permission
            {
                Code = "SCHEDULES.EDIT", Name = "schedules.edit"
            };
            setup.AddRange(master, foreign, actor, adminRole, target, otherRole,
                membership, permission, unrelatedPermission);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var writer = new PlatformRoleWriteExecutor(
                new AuthorizedPlatformMasterWrite(guarded, tenant, trusted,
                    NullLogger<AuthorizedPlatformMasterWrite>.Instance),
                guarded, trusted, NullLogger<PlatformRoleWriteExecutor>.Instance);

            var command = new UpdateRolePermissionsCommand
            {
                RoleId = target.Id,
                StorageTenantId = scope,
                PermissionIds = [permission.Id]
            };
            await writer.ExecuteAsync(principal, "roles.permissions.replace",
                async () => await new UpdateRolePermissionsCommandHandler(guarded)
                    .Handle(command, CancellationToken.None), target.Id,
                expectedPermissionIds: command.PermissionIds);

            var applied = await guarded.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().SingleAsync(x => x.RoleId == target.Id);
            Assert.Equal(scope, applied.TenantId);
            Assert.Equal(permission.Id, applied.PermissionId);
            Assert.Equal(1, await CountAuditEventsAsync(guarded, "RolePermission"));

            // Replacing the same permission set is a semantic no-op:
            // the legacy handler rewrites its rows, but must emit no new audit.
            await writer.ExecuteAsync(principal, "roles.permissions.replace",
                async () => await new UpdateRolePermissionsCommandHandler(guarded)
                    .Handle(command, CancellationToken.None), target.Id,
                expectedPermissionIds: command.PermissionIds);
            Assert.Equal(1, await CountAuditEventsAsync(guarded, "RolePermission"));

            // Regression: a pre-tracked permission for the same role must
            // not piggyback through the legacy handler's SaveChanges.
            guarded.RolePermissions.Add(new RolePermission(
                scope, target.Id, unrelatedPermission.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ExecuteAsync(principal, "roles.permissions.replace",
                    async () => await new UpdateRolePermissionsCommandHandler(guarded)
                        .Handle(command, CancellationToken.None), target.Id,
                    expectedPermissionIds: command.PermissionIds));
            guarded.ChangeTracker.Clear();
            Assert.Equal(1, await CountAuditEventsAsync(guarded, "RolePermission"));
            Assert.False(await guarded.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.RoleId == target.Id &&
                    x.PermissionId == unrelatedPermission.Id));

            // Removing the permission generates one revoke audit event.
            command.PermissionIds = [];
            await writer.ExecuteAsync(principal, "roles.permissions.replace",
                async () => await new UpdateRolePermissionsCommandHandler(guarded)
                    .Handle(command, CancellationToken.None), target.Id,
                expectedPermissionIds: command.PermissionIds);
            Assert.Equal(2, await CountAuditEventsAsync(guarded, "RolePermission"));
            Assert.False(await guarded.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.RoleId == target.Id));

            // If the audit insert fails, the replacement grant must not commit.
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_PlatformGrantAuditEntries_TestFailRole]
                ON [dbo].[PlatformGrantAuditEntries]
                AFTER INSERT AS
                BEGIN
                    THROW 51003, 'Injected RolePermission audit failure', 1;
                END
                """);
            command.PermissionIds = [permission.Id];
            try
            {
                await Assert.ThrowsAsync<SqlException>(() => writer.ExecuteAsync(
                    principal, "roles.permissions.replace",
                    async () => await new UpdateRolePermissionsCommandHandler(guarded)
                        .Handle(command, CancellationToken.None), target.Id,
                    expectedPermissionIds: command.PermissionIds));
            }
            finally
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_PlatformGrantAuditEntries_TestFailRole]");
                guarded.ChangeTracker.Clear();
            }
            Assert.False(await guarded.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.RoleId == target.Id));
            Assert.Equal(2, await CountAuditEventsAsync(guarded, "RolePermission"));

            // Recovery after an audit failure must still grant and audit normally.
            await writer.ExecuteAsync(principal, "roles.permissions.replace",
                async () => await new UpdateRolePermissionsCommandHandler(guarded)
                    .Handle(command, CancellationToken.None), target.Id,
                expectedPermissionIds: command.PermissionIds);
            Assert.Equal(3, await CountAuditEventsAsync(guarded, "RolePermission"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "roles.permissions.replace", () => Task.CompletedTask, otherRole.Id));

            // No prior assignment must be lost if the request contains an invalid permission.
            command.PermissionIds = [Guid.NewGuid()];
            await Assert.ThrowsAnyAsync<Exception>(() => writer.ExecuteAsync(
                principal, "roles.permissions.replace",
                async () => await new UpdateRolePermissionsCommandHandler(guarded)
                    .Handle(command, CancellationToken.None), target.Id,
                expectedPermissionIds: command.PermissionIds));

            Assert.Equal(permission.Id, (await guarded.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().SingleAsync(x => x.RoleId == target.Id)).PermissionId);
            Assert.False(trusted.AllowsPlatformTenant(scope));
        });
    }

    [Fact]
    public async Task Platform_master_write_requires_live_master_role_and_releases_scopes()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "platform-scope-test", "localhost");
            var foreign = new Tenant(null, "Foreign tenant", "foreign-scope-test", "localhost");
            var actor = new User(null, "scope-admin", "scope-admin@example.test",
                "Scope", "Admin", UserType.System)
            {
                NormalizedUserName = "SCOPE-ADMIN",
                NormalizedEmail = "SCOPE-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only",
                SecurityStamp = "test-security-stamp"
            };
            var role = new Role(masterId, "superadmin", "Platform admin");
            var membership = new UserRole(masterId, actor.Id, role.Id);
            setup.AddRange(master, foreign, actor, role, membership);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var access = new AuthorizedPlatformMasterWrite(guard, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);

            guard.Roles.Add(new Role(masterId, "outside-forbidden", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            using (await access.BeginAsync(principal))
            {
                var created = new Role(masterId, "platform-created", "Allowed only while scoped");
                guard.Roles.Add(created);
                await guard.SaveChangesAsync();
                Assert.Equal(masterId, created.TenantId);

                guard.Roles.Add(new Role(foreign.Id, "foreign-forbidden", null));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
                guard.ChangeTracker.Clear();
            }

            Assert.False(tenant.IsFilterDisabled);
            guard.Roles.Add(new Role(masterId, "after-scope-forbidden", null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
            guard.ChangeTracker.Clear();

            // A stale JWT and TenantContext claiming SuperAdmin cannot bypass
            // an actual role revocation in the persisted master definition scope.
            setup.UserRoles.Remove(membership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.BeginAsync(principal));
            Assert.False(tenant.IsFilterDisabled);

            // An account with an injected tenant-id claim is not System context.
            var forged = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim("tenant_id", foreign.Id.ToString()),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.BeginAsync(forged));
        });
    }


    [Fact]
    public async Task Permission_mutations_protect_platform_catalog_and_recheck_live_admin()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "permission-guard-master", "localhost");
            var actor = new User(null, "permission-guard-admin", "permission-guard-admin@example.test",
                "Permission", "Admin", UserType.System)
            {
                NormalizedUserName = "PERMISSION-GUARD-ADMIN",
                NormalizedEmail = "PERMISSION-GUARD-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only",
                SecurityStamp = "test-security-stamp"
            };
            var role = new Role(masterId, "superadmin", "Master operator");
            var membership = new UserRole(masterId, actor.Id, role.Id);
            var reserved = new Permission { Code = "ROLES.CREATE", Name = "System create Role" };
            var custom = new Permission { Code = "SERVICES.VIEW", Name = "Custom service view" };
            setup.AddRange(master, actor, role, membership, reserved, custom);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var gateway = new AuthorizedPlatformMasterWrite(guard, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);
            var writer = new PlatformPermissionWriteExecutor(gateway, guard,
                NullLogger<PlatformPermissionWriteExecutor>.Instance);

            var updated = await writer.ExecuteAsync(principal, "permissions.update", async () =>
            {
                var entry = await guard.Permissions.SingleAsync(x => x.Id == custom.Id);
                entry.Description = "Edited by verified platform operation";
                await guard.SaveChangesAsync();
                return entry.Id;
            }, permissionId: custom.Id);
            Assert.Equal(custom.Id, updated);
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));

            // Catalog entries cannot be created, renamed, deleted or deactivated
            // through the general-purpose HTTP/CQRS administrative command path.
            var ran = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "permissions.create",
                () => { ran = true; return Task.CompletedTask; },
                newCode: " roles.create "));
            Assert.False(ran);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "permissions.update", () => Task.CompletedTask,
                newCode: "SERVICES.VIEW", permissionId: reserved.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "permissions.update", () => Task.CompletedTask,
                newCode: "permissions.delete", permissionId: custom.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "permissions.change-state", () => Task.CompletedTask,
                permissionId: reserved.Id));
            Assert.False(tenant.IsFilterDisabled);

            setup.UserRoles.Remove(membership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(
                principal, "permissions.delete", () => Task.CompletedTask,
                permissionId: custom.Id));
            Assert.False(tenant.IsFilterDisabled);
        });
    }

    [Fact]
    public async Task Platform_group_membership_requires_live_admin_and_master_scoped_system_users()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "group-members-master", "localhost");
            var foreign = new Tenant(null, "Tenant", "group-members-foreign", "localhost");
            var actor = new User(null, "group-admin", "group-admin@example.test",
                "Group", "Admin", UserType.System)
            {
                NormalizedUserName = "GROUP-ADMIN",
                NormalizedEmail = "GROUP-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only-stamp"
            };
            var member = new User(null, "group-member", "group-member@example.test",
                "Group", "Member", UserType.System)
            {
                NormalizedUserName = "GROUP-MEMBER",
                NormalizedEmail = "GROUP-MEMBER@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only-stamp"
            };
            var anotherMember = new User(null, "group-member-2", "group-member-2@example.test",
                "Group", "MemberTwo", UserType.System)
            {
                NormalizedUserName = "GROUP-MEMBER-2",
                NormalizedEmail = "GROUP-MEMBER-2@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only-stamp"
            };
            var tenantUser = new User(foreign.Id, "tenant-member", "tenant-member@example.test",
                "Tenant", "Member", UserType.Tenant)
            {
                NormalizedUserName = "TENANT-MEMBER",
                NormalizedEmail = "TENANT-MEMBER@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only-stamp"
            };
            var superAdmin = new Role(masterId, "superadmin", "Privileged System role");
            var adminMembership = new UserRole(masterId, actor.Id, superAdmin.Id);
            var ordinaryGroup = new Group { TenantId = masterId, Name = "Operators", NormalizedName = "OPERATORS" };
            var privilegedGroup = new Group { TenantId = masterId, Name = "Administrators", NormalizedName = "ADMINISTRATORS" };
            var foreignGroup = new Group { TenantId = foreign.Id, Name = "Foreign", NormalizedName = "FOREIGN" };
            var privilegedLink = new GroupRole
            {
                TenantId = masterId, GroupId = privilegedGroup.Id, RoleId = superAdmin.Id
            };
            setup.AddRange(master, foreign, actor, member, anotherMember, tenantUser, superAdmin,
                adminMembership, ordinaryGroup, privilegedGroup, foreignGroup, privilegedLink);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var writer = new PlatformGroupMembershipWriteExecutor(
                new AuthorizedPlatformMasterWrite(guard, tenant, trusted,
                    NullLogger<AuthorizedPlatformMasterWrite>.Instance),
                guard, trusted, NullLogger<PlatformGroupMembershipWriteExecutor>.Instance);

            async Task AddMember(Guid groupId, Guid userId)
            {
                var command = new AddGroupMembersCommand
                {
                    StorageTenantId = masterId, GroupId = groupId, UserIds = [userId]
                };
                await writer.ExecuteAsync(principal, groupId, command.UserIds,
                    async () => await new AddGroupMembersCommandHandler(guard)
                        .Handle(command, CancellationToken.None));
            }

            await AddMember(ordinaryGroup.Id, member.Id);
            await AddMember(ordinaryGroup.Id, member.Id); // idempotent
            var stored = await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.GroupId == ordinaryGroup.Id).ToListAsync();
            Assert.Single(stored);
            Assert.Equal(masterId, stored[0].TenantId);
            Assert.Equal(member.Id, stored[0].UserId);
            Assert.Equal(1, await CountAuditEventsAsync(guard, "UserGroup"));

            // Regression: an unrelated but same-group tracked member cannot
            // hitchhike through a valid idempotent membership request.
            guard.UserGroups.Add(new UserGroup
            {
                TenantId = masterId, GroupId = ordinaryGroup.Id,
                UserId = anotherMember.Id
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                AddMember(ordinaryGroup.Id, member.Id));
            guard.ChangeTracker.Clear();
            Assert.False(await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.GroupId == ordinaryGroup.Id &&
                    x.UserId == anotherMember.Id));
            Assert.Equal(1, await CountAuditEventsAsync(guard, "UserGroup"));

            // A broken audit insert must roll back a newly-added membership.
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_PlatformGrantAuditEntries_TestFailMember]
                ON [dbo].[PlatformGrantAuditEntries]
                AFTER INSERT AS
                BEGIN
                    THROW 51004, 'Injected UserGroup audit failure', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    AddMember(ordinaryGroup.Id, anotherMember.Id));
            }
            finally
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_PlatformGrantAuditEntries_TestFailMember]");
                guard.ChangeTracker.Clear();
            }
            Assert.False(await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.GroupId == ordinaryGroup.Id &&
                    x.UserId == anotherMember.Id));
            Assert.Equal(1, await CountAuditEventsAsync(guard, "UserGroup"));

            await AddMember(ordinaryGroup.Id, anotherMember.Id);
            Assert.Equal(2, await CountAuditEventsAsync(guard, "UserGroup"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                AddMember(foreignGroup.Id, member.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                AddMember(ordinaryGroup.Id, tenantUser.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                AddMember(privilegedGroup.Id, member.Id));
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));

            // Removal is a distinct, pair-bound System operation. Idempotent
            // deletes emit no extra change event, and the actor is persisted.
            var remover = new PlatformGroupMembershipRemovalService(
                guard,
                new AuthorizedPlatformMasterWrite(guard, tenant, trusted,
                    NullLogger<AuthorizedPlatformMasterWrite>.Instance),
                trusted, NullLogger<PlatformGroupMembershipRemovalService>.Instance);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, ordinaryGroup.Id, actor.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, foreignGroup.Id, member.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, privilegedGroup.Id, member.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, ordinaryGroup.Id, tenantUser.Id));
            Assert.Equal(2, await CountAuditEventsAsync(guard, "UserGroup"));

            // Fail an audit INSERT after an actual delete; the membership must
            // survive since the change and history share one transaction.
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_PlatformGrantAuditEntries_TestFailRevoke]
                ON [dbo].[PlatformGrantAuditEntries]
                AFTER INSERT AS
                BEGIN
                    THROW 51005, 'Injected member revoke audit failure', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    remover.RevokeAsync(principal, ordinaryGroup.Id, anotherMember.Id));
            }
            finally
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_PlatformGrantAuditEntries_TestFailRevoke]");
                guard.ChangeTracker.Clear();
            }
            Assert.True(await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.GroupId == ordinaryGroup.Id &&
                    x.UserId == anotherMember.Id));
            Assert.Equal(2, await CountAuditEventsAsync(guard, "UserGroup"));

            // A previously tracked, unrelated same-group delete must be
            // rejected by the stricter GroupId/UserId grant intent.
            var differentLink = await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.GroupId == ordinaryGroup.Id && x.UserId == member.Id);
            guard.UserGroups.Remove(differentLink);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, ordinaryGroup.Id, anotherMember.Id));
            guard.ChangeTracker.Clear();
            Assert.Equal(2, await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().CountAsync(x => x.GroupId == ordinaryGroup.Id));
            Assert.Equal(2, await CountAuditEventsAsync(guard, "UserGroup"));

            Assert.True(await remover.RevokeAsync(
                principal, ordinaryGroup.Id, anotherMember.Id));
            Assert.False(await remover.RevokeAsync(
                principal, ordinaryGroup.Id, anotherMember.Id));
            Assert.False(await guard.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.GroupId == ordinaryGroup.Id &&
                    x.UserId == anotherMember.Id));
            Assert.Equal(3, await CountAuditEventsAsync(guard, "UserGroup"));
            Assert.Equal(actor.Id, await AuditActorAsync(guard, "UserGroup"));

            // Cleanup is permitted for a now-inactive System member.
            member.IsActive = false;
            await setup.SaveChangesAsync();
            Assert.True(await remover.RevokeAsync(
                principal, ordinaryGroup.Id, member.Id));
            Assert.Equal(4, await CountAuditEventsAsync(guard, "UserGroup"));

            setup.UserRoles.Remove(adminMembership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                remover.RevokeAsync(principal, ordinaryGroup.Id, member.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                AddMember(ordinaryGroup.Id, actor.Id));
            Assert.False(trusted.AllowsPlatformTenant(masterId));
        });
    }


    [Fact]
    public async Task Platform_master_write_requires_explicit_target_grant_intent()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guard, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "grant-scope-master", "localhost");
            var actor = new User(null, "grant-admin", "grant-admin@example.test",
                "Grant", "Admin", UserType.System)
            {
                NormalizedUserName = "GRANT-ADMIN",
                NormalizedEmail = "GRANT-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-grant-stamp"
            };
            var super = new Role(masterId, "superadmin", "Root administrator");
            var operatorRole = new Role(masterId, "operator", "Nonprivileged role");
            var otherRole = new Role(masterId, "auditor", "Other role");
            var group = new Group { TenantId = masterId, Name = "Operators", NormalizedName = "OPERATORS" };
            var otherGroup = new Group { TenantId = masterId, Name = "Auditors", NormalizedName = "AUDITORS" };
            var membership = new UserRole(masterId, actor.Id, super.Id);
            var permission = new Permission { Code = "REPORTS.VIEW", Name = "View reports", IsActive = true };
            setup.AddRange(master, actor, super, operatorRole, otherRole,
                group, otherGroup, membership, permission);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var gateway = new AuthorizedPlatformMasterWrite(guard, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);
            var roleWriter = new PlatformRoleWriteExecutor(gateway, guard, trusted,
                NullLogger<PlatformRoleWriteExecutor>.Instance);
            var groupWriter = new PlatformGroupMembershipWriteExecutor(gateway, guard, trusted,
                NullLogger<PlatformGroupMembershipWriteExecutor>.Instance);

            async Task DeniedSaveAsync(Action stage)
            {
                stage();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.SaveChangesAsync());
                guard.ChangeTracker.Clear();
            }

            // A validated platform administrator does not receive blanket
            // authority to assign roles, group roles, direct user permissions,
            // or even role permissions without a target-specific intent.
            using (await gateway.BeginAsync(principal))
            {
                await DeniedSaveAsync(() => guard.UserRoles.Add(
                    new UserRole(masterId, actor.Id, operatorRole.Id)));
                await DeniedSaveAsync(() => guard.GroupRoles.Add(new GroupRole
                {
                    TenantId = masterId, GroupId = group.Id, RoleId = operatorRole.Id
                }));
                await DeniedSaveAsync(() => guard.UserPermissions.Add(new UserPermission
                {
                    TenantId = masterId, UserId = actor.Id, PermissionId = permission.Id
                }));
                await DeniedSaveAsync(() => guard.RolePermissions.Add(
                    new RolePermission(masterId, operatorRole.Id, permission.Id)));
                await DeniedSaveAsync(() => guard.UserGroups.Add(new UserGroup
                {
                    TenantId = masterId, UserId = actor.Id, GroupId = group.Id
                }));
            }

            // Even an authorized RolePermission operation cannot target a
            // different role or smuggle unrelated grant types into SaveChanges.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                roleWriter.ExecuteAsync(principal, "roles.permissions.replace",
                    async () =>
                    {
                        guard.RolePermissions.Add(new RolePermission(masterId,
                            otherRole.Id, permission.Id));
                        await guard.SaveChangesAsync();
                    }, operatorRole.Id));
            guard.ChangeTracker.Clear();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                roleWriter.ExecuteAsync(principal, "roles.permissions.replace",
                    async () =>
                    {
                        guard.UserRoles.Add(new UserRole(masterId, actor.Id, operatorRole.Id));
                        await guard.SaveChangesAsync();
                    }, operatorRole.Id));
            guard.ChangeTracker.Clear();

            // Validated GroupMember mutations cannot modify another group or
            // create a direct UserRole in the same trusted write operation.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                groupWriter.ExecuteAsync(principal, group.Id, [actor.Id], async () =>
                {
                    guard.UserGroups.Add(new UserGroup
                    {
                        TenantId = masterId, UserId = actor.Id, GroupId = otherGroup.Id
                    });
                    await guard.SaveChangesAsync();
                }));
            guard.ChangeTracker.Clear();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                groupWriter.ExecuteAsync(principal, group.Id, [actor.Id], async () =>
                {
                    guard.UserRoles.Add(new UserRole(masterId, actor.Id, operatorRole.Id));
                    await guard.SaveChangesAsync();
                }));
            guard.ChangeTracker.Clear();

            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));
        });
    }


    [Fact]
    public async Task Direct_platform_user_role_grants_are_scoped_idempotent_and_revocable()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guarded, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "direct-grant-master", "localhost");
            var foreign = new Tenant(null, "Tenant", "direct-grant-tenant", "localhost");
            var admin = new User(null, "direct-admin", "direct-admin@example.test",
                "Direct", "Admin", UserType.System)
            {
                NormalizedUserName = "DIRECT-ADMIN", NormalizedEmail = "DIRECT-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var systemUser = new User(null, "direct-user", "direct-user@example.test",
                "Direct", "User", UserType.System)
            {
                NormalizedUserName = "DIRECT-USER", NormalizedEmail = "DIRECT-USER@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var tenantUser = new User(foreign.Id, "direct-tenant", "direct-tenant@example.test",
                "Tenant", "User", UserType.Tenant)
            {
                NormalizedUserName = "DIRECT-TENANT", NormalizedEmail = "DIRECT-TENANT@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var superAdmin = new Role(masterId, "superadmin", "Bootstrap role");
            var operatorRole = new Role(masterId, "operator", "Nonprivileged role");
            var foreignRole = new Role(foreign.Id, "foreign-role", "Foreign role");
            var adminMembership = new UserRole(masterId, admin.Id, superAdmin.Id);
            setup.AddRange(master, foreign, admin, systemUser, tenantUser,
                superAdmin, operatorRole, foreignRole, adminMembership);
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(admin.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, admin.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var gateway = new AuthorizedPlatformMasterWrite(guarded, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);
            var service = new PlatformUserRoleWriteService(guarded, gateway, trusted,
                NullLogger<PlatformUserRoleWriteService>.Instance);

            // Indefinite lockout is not a valid target for a new grant.
            systemUser.IsLockedOut = true;
            systemUser.LockoutEndUtc = null;
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, true));
            systemUser.IsLockedOut = false;
            await setup.SaveChangesAsync();

            // Unrelated tracked role-grants cannot piggyback on the allowed
            // UserRole mutation or be committed as part of the same SaveChanges.
            var group = new Group
            {
                TenantId = masterId, Name = "Reviewers", NormalizedName = "REVIEWERS"
            };
            setup.Groups.Add(group);
            await setup.SaveChangesAsync();
            guarded.GroupRoles.Add(new GroupRole
            {
                TenantId = masterId, GroupId = group.Id, RoleId = operatorRole.Id
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, true));
            guarded.ChangeTracker.Clear();
            Assert.Empty(await guarded.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.UserId == systemUser.Id).ToListAsync());

            Assert.True(await service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, true));
            Assert.False(await service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, true));
            Assert.Single(await guarded.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.UserId == systemUser.Id).ToListAsync());
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));

            // Explicitly disallow self-grants, privilege escalation,
            // tenant-user targets and roles from a foreign storage tenant.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, admin.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, superAdmin.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, tenantUser.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, foreignRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, foreignRole.Id, false));

            // No denied request can silently remove a previously valid grant.
            Assert.Single(await guarded.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.UserId == systemUser.Id).ToListAsync());

            Assert.True(await service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, false));
            Assert.False(await service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, false));
            Assert.Empty(await guarded.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.UserId == systemUser.Id).ToListAsync());

            // One durable entry per changed operation; denied and duplicate
            // operations do not produce audit history.
            Assert.Equal(2, await CountAuditEventsAsync(guarded, "UserRole"));
            Assert.Equal(admin.Id, await AuditActorAsync(guarded, "UserRole"));

            // Admin authorization is rechecked on the live database for every
            // grant; a stale JWT cannot keep granting after role revocation.
            setup.UserRoles.Remove(adminMembership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ChangeAsync(principal, systemUser.Id, operatorRole.Id, true));
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));
        });
    }


    [Fact]
    public async Task Platform_group_role_grants_are_scoped_idempotent_and_revocable()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guarded, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "group-role-master", "localhost");
            var foreign = new Tenant(null, "Tenant", "group-role-foreign", "localhost");
            var actor = new User(null, "gr-admin", "gr-admin@example.test",
                "Group", "Admin", UserType.System)
            {
                NormalizedUserName = "GR-ADMIN", NormalizedEmail = "GR-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var systemUser = new User(null, "gr-member", "gr-member@example.test",
                "Group", "Member", UserType.System)
            {
                NormalizedUserName = "GR-MEMBER", NormalizedEmail = "GR-MEMBER@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var tenantUser = new User(foreign.Id, "gr-tenant", "gr-tenant@example.test",
                "Tenant", "User", UserType.Tenant)
            {
                NormalizedUserName = "GR-TENANT", NormalizedEmail = "GR-TENANT@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var superAdmin = new Role(masterId, "superadmin", "Reserved bootstrap role");
            var operatorRole = new Role(masterId, "operator", "Normal platform role");
            var foreignRole = new Role(foreign.Id, "tenant-operator", "Foreign role");
            var adminMembership = new UserRole(masterId, actor.Id, superAdmin.Id);
            var group = new Group { TenantId = masterId, Name = "Operators", NormalizedName = "OPERATORS" };
            var actorGroup = new Group { TenantId = masterId, Name = "Self", NormalizedName = "SELF" };
            var privilegedGroup = new Group { TenantId = masterId, Name = "Privileged", NormalizedName = "PRIVILEGED" };
            var invalidGroup = new Group { TenantId = masterId, Name = "Mixed", NormalizedName = "MIXED" };
            var foreignGroup = new Group { TenantId = foreign.Id, Name = "Foreign", NormalizedName = "FOREIGN" };
            var inactiveGroup = new Group
            {
                TenantId = masterId, Name = "Inactive", NormalizedName = "INACTIVE", IsActive = false
            };
            setup.AddRange(master, foreign, actor, systemUser, tenantUser,
                superAdmin, operatorRole, foreignRole, adminMembership,
                group, actorGroup, privilegedGroup, invalidGroup, foreignGroup, inactiveGroup);
            setup.UserGroups.AddRange(
                new UserGroup { TenantId = masterId, UserId = systemUser.Id, GroupId = group.Id },
                new UserGroup { TenantId = masterId, UserId = actor.Id, GroupId = actorGroup.Id },
                new UserGroup { TenantId = masterId, UserId = tenantUser.Id, GroupId = invalidGroup.Id });
            setup.GroupRoles.Add(new GroupRole
            {
                TenantId = masterId, GroupId = privilegedGroup.Id, RoleId = superAdmin.Id
            });
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var gateway = new AuthorizedPlatformMasterWrite(guarded, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);
            var writer = new PlatformGroupRoleWriteService(guarded, gateway, trusted,
                NullLogger<PlatformGroupRoleWriteService>.Instance);

            Assert.True(await writer.ChangeAsync(principal, group.Id, operatorRole.Id, true));
            Assert.False(await writer.ChangeAsync(principal, group.Id, operatorRole.Id, true));
            var stored = await guarded.GroupRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.GroupId == group.Id).ToListAsync();
            Assert.Single(stored);
            Assert.Equal(masterId, stored[0].TenantId);
            Assert.Equal(operatorRole.Id, stored[0].RoleId);

            // Regression: the same administrator must not bypass the
            // GroupRole self-assignment check by joining the group AFTER
            // assigning its role. A failed membership must not be persisted.
            var memberWriter = new PlatformGroupMembershipWriteExecutor(
                gateway, guarded, trusted,
                NullLogger<PlatformGroupMembershipWriteExecutor>.Instance);
            var selfMembership = new AddGroupMembersCommand
            {
                StorageTenantId = masterId, GroupId = group.Id, UserIds = [actor.Id]
            };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                memberWriter.ExecuteAsync(principal, group.Id, selfMembership.UserIds,
                    async () => await new AddGroupMembersCommandHandler(guarded)
                        .Handle(selfMembership, CancellationToken.None)));
            Assert.False(await guarded.UserGroups.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.GroupId == group.Id && x.UserId == actor.Id));

            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));

            // No actor may attach a SUPERADMIN role to a group or modify an
            // existing bootstrap privileged group via ordinary REST operations.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, group.Id, superAdmin.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, group.Id, superAdmin.Id, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, privilegedGroup.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, privilegedGroup.Id, operatorRole.Id, false));

            // Group owners cannot grant themselves effective permissions by
            // modifying a group they belong to.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, actorGroup.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, actorGroup.Id, operatorRole.Id, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, foreignGroup.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, group.Id, foreignRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, invalidGroup.Id, operatorRole.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, inactiveGroup.Id, operatorRole.Id, true));

            // The entire SaveChanges is rejected when an unrelated direct
            // UserRole grant is staged during an otherwise authorized GroupRole
            // mutation; neither association may persist.
            guarded.UserRoles.Add(new UserRole(masterId, systemUser.Id, operatorRole.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, group.Id, operatorRole.Id, false));
            guarded.ChangeTracker.Clear();
            Assert.Single(await guarded.GroupRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.GroupId == group.Id).ToListAsync());
            Assert.Empty(await guarded.UserRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.UserId == systemUser.Id).ToListAsync());

            Assert.True(await writer.ChangeAsync(principal, group.Id, operatorRole.Id, false));
            Assert.False(await writer.ChangeAsync(principal, group.Id, operatorRole.Id, false));
            Assert.Empty(await guarded.GroupRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.GroupId == group.Id).ToListAsync());

            Assert.Equal(2, await CountAuditEventsAsync(guarded, "GroupRole"));
            Assert.Equal(actor.Id, await AuditActorAsync(guarded, "GroupRole"));

            // The persisted administrator role is checked for every operation,
            // irrespective of a still-valid SUPERADMIN claim on an old token.
            setup.UserRoles.Remove(adminMembership);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(principal, group.Id, operatorRole.Id, true));
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));
        });
    }


    [Fact]
    public async Task Direct_system_permission_deny_overrides_inherited_role_and_group_allows()
    {
        await WithTestDatabaseAsync(async (setup, tenant, guarded, trusted) =>
        {
            var masterId = DefaultSeedData.MasterTenant.Id;
            var master = new Tenant(masterId, null, "Platform", "direct-permission-master", "localhost");
            var foreign = new Tenant(null, "Tenant", "direct-permission-tenant", "localhost");
            var actor = new User(null, "up-admin", "up-admin@example.test",
                "Permission", "Admin", UserType.System)
            {
                NormalizedUserName = "UP-ADMIN", NormalizedEmail = "UP-ADMIN@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var subject = new User(null, "up-user", "up-user@example.test",
                "Permission", "User", UserType.System)
            {
                NormalizedUserName = "UP-USER", NormalizedEmail = "UP-USER@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var tenantUser = new User(foreign.Id, "up-tenant", "up-tenant@example.test",
                "Tenant", "User", UserType.Tenant)
            {
                NormalizedUserName = "UP-TENANT", NormalizedEmail = "UP-TENANT@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var otherAdmin = new User(null, "up-privileged", "up-privileged@example.test",
                "Privileged", "User", UserType.System)
            {
                NormalizedUserName = "UP-PRIVILEGED",
                NormalizedEmail = "UP-PRIVILEGED@EXAMPLE.TEST",
                PasswordHash = "test-only", SecurityStamp = "test-only"
            };
            var adminRole = new Role(masterId, "superadmin", "Reserved master role");
            var ordinaryRole = new Role(masterId, "operator", "Ordinary role");
            var groupRole = new Role(masterId, "reviewer", "Inherited group role");
            var group = new Group
            {
                TenantId = masterId, Name = "Reviewers", NormalizedName = "REVIEWERS"
            };
            var viewPermission = new Permission
            {
                Code = "APPOINTMENTS.VIEW", Name = "View appointments", IsActive = true
            };
            var exportPermission = new Permission
            {
                Code = "APPOINTMENTS.EXPORT", Name = "Export appointments", IsActive = true
            };
            var reserved = new Permission
            {
                Code = "ROLES.CREATE", Name = "Create privileged roles", IsActive = true
            };
            setup.AddRange(master, foreign, actor, subject, tenantUser, otherAdmin,
                adminRole, ordinaryRole, groupRole, group, viewPermission,
                exportPermission, reserved);
            var actorGrant = new UserRole(masterId, actor.Id, adminRole.Id);
            setup.UserRoles.AddRange(
                actorGrant,
                new UserRole(masterId, subject.Id, ordinaryRole.Id),
                new UserRole(masterId, otherAdmin.Id, adminRole.Id));
            setup.RolePermissions.AddRange(
                new RolePermission(masterId, ordinaryRole.Id, viewPermission.Id),
                new RolePermission(masterId, groupRole.Id, viewPermission.Id));
            setup.GroupRoles.Add(new GroupRole
            {
                TenantId = masterId, GroupId = group.Id, RoleId = groupRole.Id
            });
            setup.UserGroups.Add(new UserGroup
            {
                TenantId = masterId, UserId = subject.Id, GroupId = group.Id
            });
            await setup.SaveChangesAsync();

            tenant.InitializeSystem(actor.Id, isSuperAdmin: true);
            var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, actor.Id.ToString()),
                new Claim("account_type", "system"),
                new Claim(OpenIddictConstants.Claims.Role, "superadmin")
            ], "test"));
            var subjectPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, subject.Id.ToString()),
                new Claim("account_type", "system")
            ], "test"));
            var gateway = new AuthorizedPlatformMasterWrite(guarded, tenant, trusted,
                NullLogger<AuthorizedPlatformMasterWrite>.Instance);
            var writer = new PlatformUserPermissionWriteService(
                guarded, gateway, trusted,
                NullLogger<PlatformUserPermissionWriteService>.Instance);

            async Task<bool> CanViewAsync()
            {
                var req = new SystemPermissionRequirement("appointments", "view");
                var ctx = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
                    [req], subjectPrincipal, null);
                await new SystemPermissionHandler(guarded).HandleAsync(ctx);
                return ctx.HasSucceeded;
            }

            // Role and group both grant the permission, but a direct user
            // denial must take precedence over both inherited grants.
            Assert.True(await CanViewAsync());
            Assert.True(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, false));
            Assert.False(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, false));
            Assert.False(await CanViewAsync());

            // Switching deny -> allow replaces the unique row atomically.
            Assert.True(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, true));
            Assert.False(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, true));
            Assert.True(await CanViewAsync());

            // Removing the override restores ordinary role/group inheritance.
            Assert.True(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, null));
            Assert.False(await writer.ChangeAsync(
                adminPrincipal, subject.Id, viewPermission.Id, null));
            Assert.True(await CanViewAsync());

            // Explicit allow grants a permission absent from inherited roles.
            Assert.True(await writer.ChangeAsync(
                adminPrincipal, subject.Id, exportPermission.Id, true));
            var exportReq = new SystemPermissionRequirement("appointments", "export");
            var exportContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
                [exportReq], subjectPrincipal, null);
            await new SystemPermissionHandler(guarded).HandleAsync(exportContext);
            Assert.True(exportContext.HasSucceeded);

            // Reject privilege escalation, foreign subjects, reserved catalog
            // permissions, attempts to manipulate SuperAdmins, and self writes.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, actor.Id, viewPermission.Id, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, otherAdmin.Id, viewPermission.Id, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, tenantUser.Id, viewPermission.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, subject.Id, reserved.Id, true));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, subject.Id, reserved.Id, false));

            // Unrelated tracked grant rows must not ride along with an
            // authorized override write.
            guarded.GroupRoles.Add(new GroupRole
            {
                TenantId = masterId, GroupId = group.Id, RoleId = ordinaryRole.Id
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, subject.Id, exportPermission.Id, false));
            guarded.ChangeTracker.Clear();
            Assert.True(await guarded.UserPermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.TenantId == masterId &&
                    x.UserId == subject.Id &&
                    x.PermissionId == exportPermission.Id && x.IsGranted));
            Assert.False(await guarded.GroupRoles.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.TenantId == masterId &&
                    x.GroupId == group.Id && x.RoleId == ordinaryRole.Id));

            // Deny, replacement with Allow, clear, and explicit Export Allow
            // each create one durable audit event. Repeats and denied writes
            // must not create audit entries.
            Assert.Equal(4, await CountAuditEventsAsync(guarded, "UserPermission"));
            Assert.Equal(actor.Id, await AuditActorAsync(guarded, "UserPermission"));

            // Audit rows must not be editable or deletable via SQL either.
            await Assert.ThrowsAsync<SqlException>(() =>
                guarded.Database.ExecuteSqlRawAsync(
                    "UPDATE [dbo].[PlatformGrantAuditEntries] SET [Action] = 'forged'"));
            await Assert.ThrowsAsync<SqlException>(() =>
                guarded.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [dbo].[PlatformGrantAuditEntries]"));
            Assert.Equal(4, await CountAuditEventsAsync(guarded, "UserPermission"));

            // Fail the audit INSERT after the two SaveChanges calls for an
            // Allow -> Deny replacement. Both grant-row writes MUST roll
            // back, because the audit statement shares their transaction.
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER [dbo].[TR_PlatformGrantAuditEntries_TestFail]
                ON [dbo].[PlatformGrantAuditEntries]
                AFTER INSERT
                AS
                BEGIN
                    THROW 51002, 'Injected audit failure', 1;
                END
                """);
            try
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    writer.ChangeAsync(adminPrincipal, subject.Id, exportPermission.Id, false));
            }
            finally
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER IF EXISTS [dbo].[TR_PlatformGrantAuditEntries_TestFail]");
                guarded.ChangeTracker.Clear();
            }

            // The original Allow and exactly four earlier audit rows survived.
            Assert.True(await guarded.UserPermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x => x.TenantId == masterId &&
                    x.UserId == subject.Id &&
                    x.PermissionId == exportPermission.Id && x.IsGranted));
            Assert.Equal(4, await CountAuditEventsAsync(guarded, "UserPermission"));

            // Revoked SuperAdmin role cannot authorize a stale access token.
            setup.UserRoles.Remove(actorGrant);
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                writer.ChangeAsync(adminPrincipal, subject.Id, exportPermission.Id, null));
            Assert.False(tenant.IsFilterDisabled);
            Assert.False(trusted.AllowsPlatformTenant(masterId));
        });
    }

    private static Task<int> CountAuditEventsAsync(IdentityDbContext db, string kind) =>
        db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [dbo].[PlatformGrantAuditEntries] WHERE [Kind] = @kind",
            new SqlParameter("@kind", kind)).SingleAsync();

    private static Task<Guid> AuditActorAsync(IdentityDbContext db, string kind) =>
        db.Database.SqlQueryRaw<Guid>(
            "SELECT TOP(1) [ActorUserId] AS [Value] FROM [dbo].[PlatformGrantAuditEntries] WHERE [Kind] = @kind ORDER BY [OccurredAtUtc]",
            new SqlParameter("@kind", kind)).SingleAsync();

    private static async Task WithTestDatabaseAsync(
        Func<IdentityDbContext, TenantContext, IdentityDbContext, TrustedTenantWriteScope, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("SHINERA_SQLSERVER_TEST_CONNECTION is required");

        var cs = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = "ShineraWriteTest_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 30
        };

        var tenant = new TenantContext();
        var setupOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        await using var setup = new IdentityDbContext(setupOptions, tenant);
        var trusted = new TrustedTenantWriteScope(NullLogger<TrustedTenantWriteScope>.Instance);
        var interceptor = new TenantSaveChangesInterceptor(tenant, trusted);
        var guardedOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs.ConnectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(interceptor).Options;

        try
        {
            await setup.Database.MigrateAsync();
            await using var guarded = new IdentityDbContext(guardedOptions, tenant);
            await test(setup, tenant, guarded, trusted);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
