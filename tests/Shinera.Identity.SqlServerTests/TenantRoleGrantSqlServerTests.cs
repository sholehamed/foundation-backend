using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.SqlServerTests;

/// <summary>Real SQL migrations, optimistic CAS, immutable audit and rollback.</summary>
public sealed class TenantRoleGrantSqlServerTests
{
    [Fact]
    public async Task Owner_grants_revokes_with_version_CAS_and_append_only_audit()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (context, actor, ownRole, target, foreign) =
                await SeedAsync(setup, options);
            await using var db = context;
            var original = await GetVersionAsync(db, target);
            var first = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(original,
                    [TenantWorkspacePermissionCatalog.MembersRead]), default);
            Assert.NotEqual(original, first);
            Assert.True(await db.RolePermissions.AsNoTracking().AnyAsync(x =>
                x.RoleId == target &&
                x.TenantId == actor.ActiveTenantId &&
                x.Permission.Code == TenantWorkspacePermissionCatalog.MembersRead));
            Assert.Equal(1, await AuditCountAsync(db));

            await Assert.ThrowsAsync<TenantRoleGrantConflictException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(
                    actor, db, target,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(original,
                        [TenantWorkspacePermissionCatalog.GroupsRead]), default));
            Assert.Equal(1, await AuditCountAsync(db));

            var noop = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(first,
                    [TenantWorkspacePermissionCatalog.MembersRead]), default);
            Assert.Equal(first, noop);
            Assert.Equal(1, await AuditCountAsync(db));

            var removed = await TenantRolePermissionGrantWriter.ReplaceAsync(
                actor, db, target,
                new TenantRolePermissionGrantWriter.ReplaceRequest(first, []), default);
            Assert.NotEqual(first, removed);
            Assert.False(await db.RolePermissions.AsNoTracking().AnyAsync(
                x => x.RoleId == target && x.TenantId == actor.ActiveTenantId));
            Assert.Equal(2, await AuditCountAsync(db));
            var audit = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantRoleGrantAuditEntries] ORDER BY [OccurredAtUtc]")
                .ToArrayAsync();
            Assert.Contains("grant", audit);
            Assert.Contains("revoke", audit);

            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [dbo].[TenantRoleGrantAuditEntries]"));
            Assert.Equal(2, await AuditCountAsync(db));

            // Local OWNER cannot be edited; foreign target cannot be mutated.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, ownRole,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(removed, [])));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, foreign,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(removed, [])));
            Assert.Equal(2, await AuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Unknown_grants_and_actor_assigned_role_are_rejected()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var version = await GetVersionAsync(db, roleId);
            await Assert.ThrowsAsync<TenantRoleGrantValidationException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        ["SYSTEM.ROLES.CREATE"])));
            await Assert.ThrowsAsync<TenantRoleGrantValidationException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        [TenantWorkspacePermissionCatalog.MembersRead,
                            TenantWorkspacePermissionCatalog.MembersRead])));

            setup.UserRoles.Add(new UserRole(actor.ActiveTenantId!.Value,
                actor.UserId!.Value, roleId));
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(version,
                        [TenantWorkspacePermissionCatalog.MembersRead])));
            Assert.Empty(await db.RolePermissions.AsNoTracking().ToListAsync());
            Assert.Equal(0, await AuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Failure_of_audit_insert_rolls_back_permissions_and_role_version()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var initial = await GetVersionAsync(db, roleId);
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantRoleGrantAuditEntries]");

            await Assert.ThrowsAsync<SqlException>(() =>
                TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, roleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(initial,
                        [TenantWorkspacePermissionCatalog.GroupsRead])));
            db.ChangeTracker.Clear();
            Assert.False(await db.RolePermissions.AsNoTracking()
                .AnyAsync(x => x.RoleId == roleId));
            Assert.Equal(initial, await GetVersionAsync(db, roleId));
        });
    }


    [Fact]
    public async Task Owner_can_create_edit_and_disable_role_with_audit_and_live_revocation()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;

            var newRole = await TenantRoleLifecycleWriter.CreateAsync(actor, db,
                new TenantRoleLifecycleWriter.CreateRequest("  Receptionist  ", "  Front desk  "));
            Assert.Equal("Receptionist", newRole.Name);
            Assert.True(newRole.IsActive);
            Assert.False(newRole.IsOwner);
            Assert.NotEqual("", newRole.Version);
            Assert.Empty(await db.RolePermissions.AsNoTracking()
                .Where(x => x.RoleId == newRole.Id).ToListAsync());

            var initial = newRole.Version;
            var renamed = await TenantRoleLifecycleWriter.UpdateAsync(actor, db, newRole.Id,
                new TenantRoleLifecycleWriter.UpdateRequest(
                    "Front Desk", "New description", initial));
            Assert.Equal("Front Desk", renamed.Name);
            Assert.NotEqual(initial, renamed.Version);

            await Assert.ThrowsAsync<TenantRoleLifecycleConflictException>(() =>
                TenantRoleLifecycleWriter.UpdateAsync(actor, db, newRole.Id,
                    new TenantRoleLifecycleWriter.UpdateRequest("Stale Name", null, initial)));

            var noChange = await TenantRoleLifecycleWriter.UpdateAsync(
                actor, db, newRole.Id,
                new TenantRoleLifecycleWriter.UpdateRequest(
                    renamed.Name, "New description", renamed.Version));
            Assert.Equal(renamed.Version, noChange.Version);

            // FR-12B grants now work on a role created by FR-12C.
            await TenantRolePermissionGrantWriter.ReplaceAsync(actor, db, newRole.Id,
                new TenantRolePermissionGrantWriter.ReplaceRequest(renamed.Version,
                    [TenantWorkspacePermissionCatalog.MembersRead]));
            var versionAfterGrant = await GetVersionAsync(db, newRole.Id);
            var user = new User(Guid.NewGuid(), actor.ActiveTenantId!.Value,
                "staff-role", "staff-role@example.test", "Staff", "Member", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "STAFF-ROLE",
                NormalizedEmail = "STAFF-ROLE@EXAMPLE.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(user);
            setup.UserRoles.Add(new UserRole(actor.ActiveTenantId!.Value, user.Id, newRole.Id));
            await setup.SaveChangesAsync();

            var staff = new TenantContext();
            staff.InitializeTenant(user.Id, actor.ActiveTenantId.Value,
                [actor.ActiveTenantId.Value], [], actor.ActiveTenantId.Value);
            await using var staffDb = new IdentityDbContext(options, staff);
            Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                staff, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

            var disabled = await TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, newRole.Id,
                new TenantRoleLifecycleWriter.ChangeStateRequest(false, versionAfterGrant));
            Assert.False(disabled.IsActive);
            Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                staff, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
            Assert.True(await db.RolePermissions.AsNoTracking()
                .AnyAsync(x => x.RoleId == newRole.Id));

            var enabled = await TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, newRole.Id,
                new TenantRoleLifecycleWriter.ChangeStateRequest(true, disabled.Version));
            Assert.True(enabled.IsActive);
            Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                staff, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

            var audit = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantRoleChangeAuditEntries]")
                .ToListAsync();
            Assert.Equal(4, audit.Count); // create, update, disable, enable
            Assert.Contains("create", audit);
            Assert.Contains("update", audit);
            Assert.Equal(2, audit.Count(x => x == "state"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [dbo].[TenantRoleChangeAuditEntries]"));
            Assert.Equal(4, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantRoleChangeAuditEntries]")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Role_lifecycle_protects_owner_cross_tenant_reserved_name_and_stale_state()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, owner, otherRole, foreign) = await SeedAsync(setup, options);
            await using var db = ctx;
            await Assert.ThrowsAsync<TenantRoleLifecycleValidationException>(() =>
                TenantRoleLifecycleWriter.CreateAsync(actor, db,
                    new TenantRoleLifecycleWriter.CreateRequest("  oWnEr ", null)));
            await Assert.ThrowsAsync<TenantRoleLifecycleValidationException>(() =>
                TenantRoleLifecycleWriter.CreateAsync(actor, db,
                    new TenantRoleLifecycleWriter.CreateRequest(" SUPERADMIN ", null)));

            var role = await TenantRoleLifecycleWriter.CreateAsync(actor, db,
                new TenantRoleLifecycleWriter.CreateRequest("Front desk", null));
            await Assert.ThrowsAsync<TenantRoleLifecycleConflictException>(() =>
                TenantRoleLifecycleWriter.CreateAsync(actor, db,
                    new TenantRoleLifecycleWriter.CreateRequest("FRONT DESK", "duplicate")));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRoleLifecycleWriter.UpdateAsync(actor, db, owner,
                    new TenantRoleLifecycleWriter.UpdateRequest("Renamed", null, role.Version)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, foreign,
                    new TenantRoleLifecycleWriter.ChangeStateRequest(false, role.Version)));

            var first = await TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, role.Id,
                new TenantRoleLifecycleWriter.ChangeStateRequest(false, role.Version));
            await Assert.ThrowsAsync<TenantRoleLifecycleConflictException>(() =>
                TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, role.Id,
                    new TenantRoleLifecycleWriter.ChangeStateRequest(true, role.Version)));

            // An actor who joins the target role cannot alter it even if Owner
            // permission remains otherwise valid.
            setup.UserRoles.Add(new UserRole(actor.ActiveTenantId!.Value,
                actor.UserId!.Value, otherRole));
            await setup.SaveChangesAsync();
            var version = await GetVersionAsync(db, otherRole);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantRoleLifecycleWriter.ChangeStateAsync(actor, db, otherRole,
                    new TenantRoleLifecycleWriter.ChangeStateRequest(false, version)));
            Assert.False(first.IsActive);
        });
    }

    [Fact]
    public async Task Role_lifecycle_audit_failure_rolls_back_created_role()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantRoleChangeAuditEntries]");
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantRoleLifecycleWriter.CreateAsync(actor, db,
                    new TenantRoleLifecycleWriter.CreateRequest("NoAudit", null)));
            db.ChangeTracker.Clear();
            Assert.False(await db.Roles.AsNoTracking()
                .AnyAsync(x => x.Name == "NoAudit"));
        });
    }


    [Fact]
    public async Task Owner_assigns_revokes_member_roles_and_effective_access_tracks_live_links()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, ownerRole, ordinaryRole, foreignRole) =
                await SeedAsync(setup, options);
            await using var db = ctx;
            var tenantId = actor.ActiveTenantId!.Value;
            var member = new User(Guid.NewGuid(), tenantId, "staff-assignment",
                "staff-assignment@example.test", "Staff", "Member", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "STAFF-ASSIGNMENT",
                NormalizedEmail = "STAFF-ASSIGNMENT@EXAMPLE.TEST",
                PasswordHash = "fixture"
            };
            setup.Users.Add(member);
            await setup.SaveChangesAsync();

            var matrix = await TenantMemberRoleAssignmentWriter.ReadAsync(
                actor, db, member.Id, default);
            Assert.NotNull(matrix);
            Assert.True(matrix.CanEdit);
            Assert.False(matrix.Roles.Any(x => x.Id == ownerRole || x.Id == foreignRole));
            Assert.Contains(matrix.Roles, x => x.Id == ordinaryRole && !x.Assigned);

            var version = matrix.Version;
            var granted = await TenantMemberRoleAssignmentWriter.ReplaceAsync(
                actor, db, member.Id,
                new TenantMemberRoleAssignmentWriter.ReplaceRequest(version, [ordinaryRole]));
            Assert.NotEqual(version, granted);
            Assert.Single(await db.UserRoles.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.UserId == member.Id)
                .ToListAsync());
            Assert.Equal(1, await MemberAuditCountAsync(db));

            // A role granted to the member contributes to effective access
            // as soon as it is assigned; no re-login / UI trust required.
            var rolePermission = await setup.Permissions.SingleAsync(p =>
                p.Code == TenantWorkspacePermissionCatalog.MembersRead);
            setup.RolePermissions.Add(new RolePermission(
                tenantId, ordinaryRole, rolePermission.Id));
            await setup.SaveChangesAsync();
            var staff = new TenantContext();
            staff.InitializeTenant(member.Id, tenantId, [tenantId], [], tenantId);
            await using var staffDb = new IdentityDbContext(options, staff);
            Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                staff, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

            await Assert.ThrowsAsync<TenantMemberRoleConflictException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(version, [])));
            Assert.Equal(1, await MemberAuditCountAsync(db));
            var noChange = await TenantMemberRoleAssignmentWriter.ReplaceAsync(
                actor, db, member.Id,
                new TenantMemberRoleAssignmentWriter.ReplaceRequest(granted, [ordinaryRole]));
            Assert.Equal(granted, noChange);
            Assert.Equal(1, await MemberAuditCountAsync(db));

            var revoked = await TenantMemberRoleAssignmentWriter.ReplaceAsync(
                actor, db, member.Id,
                new TenantMemberRoleAssignmentWriter.ReplaceRequest(granted, []));
            Assert.NotEqual(granted, revoked);
            Assert.False(await db.UserRoles.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.UserId == member.Id));
            Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                staff, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
            Assert.Equal(2, await MemberAuditCountAsync(db));

            var audit = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantMemberRoleAuditEntries]")
                .ToListAsync();
            Assert.Contains("grant", audit);
            Assert.Contains("revoke", audit);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [dbo].[TenantMemberRoleAuditEntries]"));
            Assert.Equal(2, await MemberAuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Member_roles_reject_self_owner_foreign_and_inactive_targets()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, ownerRole, ordinaryRole, foreignRole) =
                await SeedAsync(setup, options);
            await using var db = ctx;
            var tenantId = actor.ActiveTenantId!.Value;
            var member = new User(Guid.NewGuid(), tenantId, "target-member",
                "target-member@example.test", "Target", "Member", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "TARGET-MEMBER",
                NormalizedEmail = "TARGET-MEMBER@EXAMPLE.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(member);
            await setup.SaveChangesAsync();
            var version = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            var req = new TenantMemberRoleAssignmentWriter.ReplaceRequest(
                version, [ordinaryRole]);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db,
                    actor.UserId!.Value, req));
            await Assert.ThrowsAsync<TenantMemberRoleValidationException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(version, [ownerRole])));
            await Assert.ThrowsAsync<TenantMemberRoleValidationException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(version, [foreignRole])));
            await Assert.ThrowsAsync<TenantMemberRoleValidationException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(version,
                        [ordinaryRole, ordinaryRole])));

            setup.UserRoles.Add(new UserRole(tenantId, member.Id, ownerRole));
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id, req));
            // The seed DbContext has no Tenant read scope; bypass its read
            // filter explicitly for this fixture cleanup, never in the API.
            setup.UserRoles.Remove(await setup.UserRoles.IgnoreQueryFilters(["tenant"])
                .SingleAsync(x => x.TenantId == tenantId &&
                    x.UserId == member.Id && x.RoleId == ownerRole));
            await setup.SaveChangesAsync();

            member.IsActive = false;
            await setup.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id, req));
            Assert.Equal(0, await MemberAuditCountAsync(db));
        });
    }

    [Fact]
    public async Task Member_role_audit_failure_rolls_back_membership_and_user_rowversion()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, ordinaryRole, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var tenantId = actor.ActiveTenantId!.Value;
            var member = new User(Guid.NewGuid(), tenantId, "rollback-member",
                "rollback-member@example.test", "Rollback", "Member", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "ROLLBACK-MEMBER",
                NormalizedEmail = "ROLLBACK-MEMBER@EXAMPLE.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(member);
            await setup.SaveChangesAsync();
            var version = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberRoleAuditEntries]");
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(
                        version, [ordinaryRole])));
            db.ChangeTracker.Clear();
            Assert.False(await db.UserRoles.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.UserId == member.Id));
            var currentVersion = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            Assert.Equal(version, currentVersion);
        });
    }


    [Fact]
    public async Task Inactive_role_cannot_be_newly_granted_but_can_be_revoked_and_registry_must_be_reviewed()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var tenantId = actor.ActiveTenantId!.Value;
            var member = new User(Guid.NewGuid(), tenantId, "inactive-target",
                "inactive-target@example.test", "Inactive", "Role", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "INACTIVE-TARGET",
                NormalizedEmail = "INACTIVE-TARGET@EXAMPLE.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(member);
            var inactiveRole = await setup.Roles.SingleAsync(x => x.Id == roleId);
            inactiveRole.IsActive = false;
            await setup.SaveChangesAsync();

            var token = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            await Assert.ThrowsAsync<TenantMemberRoleConflictException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(token, [roleId])));

            // A historical inactive membership can be retained or revoked,
            // but not added. Create only through the unscoped test seed.
            setup.UserRoles.Add(new UserRole(tenantId, member.Id, roleId));
            await setup.SaveChangesAsync();
            var matrix = await TenantMemberRoleAssignmentWriter.ReadAsync(
                actor, db, member.Id, default);
            Assert.NotNull(matrix);
            Assert.Contains(matrix.Roles, x => x.Id == roleId &&
                !x.IsActive && x.Assigned);
            await TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                new TenantMemberRoleAssignmentWriter.ReplaceRequest(token, []));
            Assert.Equal(1, await MemberAuditCountAsync(db));

            // Owner fallback on a missing catalog definition must never open
            // this NEW privilege-changing operation.
            var actionUi = await setup.UiResources.SingleAsync(x =>
                x.Key == "tenant.workspace.members.assign-roles");
            actionUi.IsActive = false;
            await setup.SaveChangesAsync();
            Assert.False(await TenantMemberRoleAssignmentWriter.CanManageAsync(actor, db, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantMemberRoleAssignmentWriter.ReplaceAsync(actor, db, member.Id,
                    new TenantMemberRoleAssignmentWriter.ReplaceRequest(token, [])));
        });
    }


    [Fact]
    public async Task Real_SQL_guarded_context_denies_direct_grants_but_both_audited_writers_succeed()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (plain, actor, _, roleId, _) = await SeedAsync(setup, options);
            await using var unguarded = plain;
            var tenantId = actor.ActiveTenantId!.Value;
            var member = new User(Guid.NewGuid(), tenantId, "guarded-target",
                "guarded-target@example.test", "Guarded", "Member", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "GUARDED-TARGET",
                NormalizedEmail = "GUARDED-TARGET@EXAMPLE.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(member);
            await setup.SaveChangesAsync();

            var trusted = new TrustedTenantWriteScope(
                NullLogger<TrustedTenantWriteScope>.Instance);
            var interceptor = new TenantSaveChangesInterceptor(actor, trusted);
            var protectedOptions = new DbContextOptionsBuilder<IdentityDbContext>(options)
                .AddInterceptors(interceptor).Options;
            await using var db = new IdentityDbContext(protectedOptions, actor);

            // Both relationship types fail closed without a reviewed intent.
            var memberPermission = await setup.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.MembersRead);
            db.UserRoles.Add(new UserRole(tenantId, member.Id, roleId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.RolePermissions.Add(new RolePermission(
                tenantId, roleId, memberPermission.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // The previously-reviewed SQL writers still operate and audit,
            // but only through their precise temporary storage intents.
            var roleVersion = await GetVersionAsync(db, roleId);
            // Invoke the REAL endpoint handler, not only the service:
            // the HTTP binding must supply the scoped write grant.
            var roleResponse = await TenantWorkspace.ReplaceRolePermissions(
                actor, db, new DefaultHttpContext(), roleId,
                new TenantRolePermissionGrantWriter.ReplaceRequest(roleVersion,
                    [TenantWorkspacePermissionCatalog.MembersRead]),
                trusted, default);
            Assert.Equal(StatusCodes.Status200OK,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(roleResponse).StatusCode);
            var nextRoleVersion = await GetVersionAsync(db, roleId);
            Assert.NotEqual(roleVersion, nextRoleVersion);
            Assert.Equal(1, await AuditCountAsync(db));
            Assert.True(await db.RolePermissions.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.RoleId == roleId &&
                x.PermissionId == memberPermission.Id));

            var memberVersion = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            var memberResponse = await TenantWorkspace.ReplaceMemberRoles(
                actor, db, new DefaultHttpContext(), member.Id,
                new TenantMemberRoleAssignmentWriter.ReplaceRequest(
                    memberVersion, [roleId]), trusted, default);
            Assert.Equal(StatusCodes.Status200OK,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(memberResponse).StatusCode);
            var nextMemberVersion = Convert.ToBase64String((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).RowVersion);
            Assert.NotEqual(memberVersion, nextMemberVersion);
            Assert.Equal(1, await MemberAuditCountAsync(db));
            Assert.True(await db.UserRoles.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.UserId == member.Id && x.RoleId == roleId));

            // After the auditable operation, the scope has been disposed;
            // no unrelated grant may piggyback on the existing DbContext.
            db.RolePermissions.Add(new RolePermission(
                tenantId, roleId, Guid.NewGuid()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => db.SaveChangesAsync());
        });
    }


    [Fact]
    public async Task Pending_member_is_inactive_uncredentialed_unassigned_and_audited()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, owner, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var hasher = new PasswordHasher<User>();
            Assert.True(await TenantPendingMemberWriter.CanCreateAsync(owner, db, default));
            var response = await TenantWorkspace.CreatePendingMember(owner, db,
                new DefaultHttpContext(), hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "  09121112233  ", "staff@local.test", "  Local ", " Staff "), default);
            Assert.Equal(StatusCodes.Status200OK,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            var member = await db.Users.AsNoTracking().SingleAsync(x =>
                x.TenantId == owner.ActiveTenantId && x.NormalizedUserName == "09121112233");
            Assert.False(member.IsActive);
            Assert.False(member.EmailConfirmed);
            Assert.Equal(UserType.Tenant, member.UserType);
            Assert.Equal(owner.ActiveTenantId, member.TenantId);
            Assert.Equal("Local", member.FirstName);
            Assert.Equal("Staff", member.LastName);
            Assert.False(string.IsNullOrEmpty(member.PasswordHash));
            Assert.Equal(PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(member, member.PasswordHash, "password"));
            Assert.False(await db.UserRoles.AsNoTracking().AnyAsync(x => x.UserId == member.Id));
            Assert.False(await db.UserGroups.AsNoTracking().AnyAsync(x => x.UserId == member.Id));
            Assert.False(await db.UserPermissions.AsNoTracking().AnyAsync(x => x.UserId == member.Id));
            var events = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantMemberLifecycleAuditEntries]")
                .ToArrayAsync();
            Assert.Equal(["create_inactive"], events);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [dbo].[TenantMemberLifecycleAuditEntries]"));
        });
    }

    [Fact]
    public async Task Pending_member_rejects_duplicates_invalid_inputs_missing_grants_and_nonowner()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, owner, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var hasher = new PasswordHasher<User>();
            var valid = new TenantPendingMemberWriter.CreateRequest(
                "staff.new", "staff.new@local.test", "First", "Last");
            await Assert.ThrowsAsync<TenantMemberCreateValidationException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db, hasher,
                    new TenantPendingMemberWriter.CreateRequest(
                        "a", "bad", "First", "Last")));
            await TenantPendingMemberWriter.CreateAsync(owner, db, hasher, valid);
            await Assert.ThrowsAsync<TenantMemberCreateConflictException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db, hasher,
                    new TenantPendingMemberWriter.CreateRequest(
                        "staff.new", "alternate@local.test", "First", "Last")));
            await Assert.ThrowsAsync<TenantMemberCreateConflictException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db, hasher,
                    new TenantPendingMemberWriter.CreateRequest(
                        "staff.other", "staff.new@local.test", "First", "Last")));

            var staff = new User(Guid.NewGuid(), owner.ActiveTenantId!.Value,
                "nonowner", "nonowner@local.test", "Not", "Owner", UserType.Tenant)
            {
                IsActive = true, NormalizedUserName = "NONOWNER",
                NormalizedEmail = "NONOWNER@LOCAL.TEST", PasswordHash = "fixture"
            };
            setup.Users.Add(staff);
            await setup.SaveChangesAsync();
            var staffContext = new TenantContext();
            staffContext.InitializeTenant(staff.Id, owner.ActiveTenantId.Value,
                [owner.ActiveTenantId.Value], [owner.ActiveTenantId.Value],
                owner.ActiveTenantId.Value);
            await using var nonownerDb = new IdentityDbContext(options, staffContext);
            Assert.False(await TenantPendingMemberWriter.CanCreateAsync(
                staffContext, nonownerDb, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantPendingMemberWriter.CreateAsync(staffContext, nonownerDb, hasher, valid));

            var action = await setup.UiResources.SingleAsync(x =>
                x.Key == "tenant.workspace.members.create");
            action.IsActive = false;
            await setup.SaveChangesAsync();
            Assert.False(await TenantPendingMemberWriter.CanCreateAsync(owner, db, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db, hasher, valid));
        });
    }

    [Fact]
    public async Task Pending_member_rejects_extra_UI_links_and_direct_owner_deny()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, owner, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            var create = await setup.Permissions.SingleAsync(p =>
                p.Code == TenantWorkspacePermissionCatalog.MembersCreate);
            var otherUi = await setup.UiResources.SingleAsync(u =>
                u.Key == "tenant.workspace.groups");
            setup.PermissionUiResources.Add(
                new PermissionUiResource(create.Id, otherUi.Id));
            await setup.SaveChangesAsync();
            Assert.False(await TenantPendingMemberWriter.CanCreateAsync(
                owner, db, default));

            setup.PermissionUiResources.Remove(await setup.PermissionUiResources
                .SingleAsync(x => x.PermissionId == create.Id &&
                                  x.UiResourceId == otherUi.Id));
            await setup.SaveChangesAsync();
            Assert.True(await TenantPendingMemberWriter.CanCreateAsync(
                owner, db, default));

            setup.UserPermissions.Add(new UserPermission
            {
                TenantId = owner.ActiveTenantId!.Value,
                UserId = owner.UserId!.Value,
                PermissionId = create.Id,
                IsGranted = false
            });
            await setup.SaveChangesAsync();
            Assert.False(await TenantPendingMemberWriter.CanCreateAsync(
                owner, db, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db,
                    new PasswordHasher<User>(),
                    new TenantPendingMemberWriter.CreateRequest(
                        "denied.staff", "denied@local.test", "Denied", "Staff")));
        });
    }

    [Fact]
    public async Task Pending_member_audit_failure_rolls_back_new_user()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (ctx, owner, _, _, _) = await SeedAsync(setup, options);
            await using var db = ctx;
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberLifecycleAuditEntries]");
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantPendingMemberWriter.CreateAsync(owner, db, new PasswordHasher<User>(),
                    new TenantPendingMemberWriter.CreateRequest(
                        "no.audit", "no.audit@local.test", "No", "Audit")));
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(x =>
                x.TenantId == owner.ActiveTenantId &&
                x.NormalizedUserName == "NO.AUDIT"));
        });
    }

    [Fact]
    public async Task Invitation_token_is_hash_only_and_activation_is_single_use()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db,
                hasher, new TenantPendingMemberWriter.CreateRequest(
                    "invite.staff", "invite.staff@example.test", "Invite", "Staff"));
            var sender = new FakeInvitationSender();
            Assert.True(await TenantMemberInvitationWriter.CanInviteAsync(actor, db, default));
            var submitted = await TenantMemberInvitationWriter.InviteAsync(
                actor, db, sender, member.Id);
            Assert.Equal("submitted", submitted.Status);
            Assert.Equal("invite.staff@example.test", sender.Recipient);
            Assert.Equal("tenant-role-grants", sender.Slug);
            Assert.Equal(64, sender.Token?.Length);
            Assert.True(await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE DATALENGTH([TokenHash])=32")
                .SingleAsync() == 1);
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE [SubmittedAtUtc] IS NULL")
                .SingleAsync());

            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var activation = new TenantMemberInvitationWriter.ActivateRequest(
                "tenant-role-grants", sender.Token, "P@ssword-For-St4ff!");
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    activation with { TenantSlug = "other-role-grants" }));
            await TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher, activation);
            anonymous.ChangeTracker.Clear();
            var user = await anonymous.Users.AsNoTracking().SingleAsync(x => x.Id == member.Id);
            Assert.True(user.IsActive);
            Assert.True(user.EmailConfirmed);
            Assert.Equal(PasswordVerificationResult.Success,
                hasher.VerifyHashedPassword(user, user.PasswordHash, "P@ssword-For-St4ff!"));
            Assert.False(await anonymous.UserRoles.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.UserId == member.Id));
            Assert.False(await anonymous.UserPermissions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.UserId == member.Id));
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher, activation));
            var actions = await anonymous.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantMemberLifecycleAuditEntries]")
                .ToArrayAsync();
            Assert.Contains("invite_issued", actions);
            Assert.Contains("invite_submitted", actions);
            Assert.Contains("invite_activated", actions);
        });
    }

    [Fact]
    public async Task Invitation_transport_failure_never_creates_usable_token()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "fail.staff", "fail.staff@example.test", "Fail", "Staff"));
            var missing = new FakeInvitationSender { IsConfigured = false };
            await Assert.ThrowsAsync<TenantInvitationDeliveryException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db, missing, member.Id));
            Assert.Null(missing.Token);
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations]").SingleAsync());

            var failed = new FakeInvitationSender { Fail = true };
            await Assert.ThrowsAsync<TenantInvitationDeliveryException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db, failed, member.Id));
            Assert.NotNull(failed.Token);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE [RevokedAtUtc] IS NOT NULL AND [SubmittedAtUtc] IS NULL")
                .SingleAsync());
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    new TenantMemberInvitationWriter.ActivateRequest(
                        "tenant-role-grants", failed.Token, "P@ssword-For-St4ff!")));
            Assert.False((await db.Users.AsNoTracking().SingleAsync(x => x.Id == member.Id)).IsActive);
        });
    }

    [Fact]
    public async Task Resending_invitation_revokes_old_token_and_blocks_owner_target()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "resend.staff", "resend.staff@example.test", "Resend", "Staff"));
            var original = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, original, member.Id);
            var resend = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, resend, member.Id);
            Assert.NotEqual(original.Token, resend.Token);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    new TenantMemberInvitationWriter.ActivateRequest(
                        "tenant-role-grants", original.Token, "P@ssword-For-St4ff!")));
            await Assert.ThrowsAsync<TenantInvitationTargetException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db, resend,
                    actor.UserId!.Value));
        });
    }

    [Fact]
    public async Task Invitation_issue_audit_failure_rolls_back_token_and_never_sends()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "audit.staff", "audit.staff@example.test", "Audit", "Staff"));
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberLifecycleAuditEntries]");
            var sender = new FakeInvitationSender();
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db, sender, member.Id));
            Assert.Null(sender.Token);
            db.ChangeTracker.Clear();
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations]").SingleAsync());
        });
    }

    [Fact]
    public async Task Invitation_activation_audit_failure_rolls_back_active_state_and_token_use()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "rollback.inv", "rollback.inv@example.test", "Rollback", "Invite"));
            var sender = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, sender, member.Id);
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberLifecycleAuditEntries]");
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    new TenantMemberInvitationWriter.ActivateRequest(
                        "tenant-role-grants", sender.Token, "P@ssword-For-St4ff!")));
            anonymous.ChangeTracker.Clear();
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
            Assert.Equal(0, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE [UsedAtUtc] IS NOT NULL")
                .SingleAsync());
        });
    }

    private sealed class FakeInvitationSender : ITenantInvitationSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Fail { get; set; }
        public string? Token { get; private set; }
        public string? Recipient { get; private set; }
        public string? Slug { get; private set; }

        public Task SendAsync(string recipientEmail, string tenantSlug, string token,
            CancellationToken cancellationToken)
        {
            Recipient = recipientEmail;
            Slug = tenantSlug;
            Token = token;
            if (Fail) throw new InvalidOperationException("Fake SMTP unavailable");
            return Task.CompletedTask;
        }
    }

    private static Task<int> MemberAuditCountAsync(IdentityDbContext db) =>
        db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberRoleAuditEntries]")
            .SingleAsync();

    private static async Task<int> AuditCountAsync(IdentityDbContext db) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantRoleGrantAuditEntries]").SingleAsync();

    private static async Task<string> GetVersionAsync(IdentityDbContext db, Guid roleId) =>
        Convert.ToBase64String((await db.Roles.AsNoTracking()
            .SingleAsync(x => x.Id == roleId)).RowVersion);

    private static async Task<(IdentityDbContext, TenantContext, Guid, Guid, Guid)>
        SeedAsync(IdentityDbContext setup, DbContextOptions<IdentityDbContext> options)
    {
        var own = new Tenant(Guid.NewGuid(), null, "Owner workspace",
            "tenant-role-grants", "owner.test") { IsActive = true };
        var other = new Tenant(Guid.NewGuid(), null, "Other workspace",
            "other-role-grants", "other.test") { IsActive = true };
        var user = new User(Guid.NewGuid(), own.Id, "owner", "owner@example.test",
            "Local", "Owner", UserType.Tenant)
        {
            IsActive = true, NormalizedUserName = "OWNER",
            NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "fixture"
        };
        var ownerRole = new Role(own.Id, "Owner", "Owner role") { Scope = RoleScope.Tenant };
        var editor = new Role(own.Id, "Operator", "Target role") { Scope = RoleScope.Tenant };
        var foreign = new Role(other.Id, "Unrelated", "Foreign") { Scope = RoleScope.Tenant };
        setup.Tenants.AddRange(own, other);
        setup.Users.Add(user);
        setup.Roles.AddRange(ownerRole, editor, foreign);
        setup.UserRoles.Add(new UserRole(own.Id, user.Id, ownerRole.Id));
        await setup.SaveChangesAsync();

        var catalog = new TenantPermissionCatalogSync(setup);
        var preview = await catalog.PreviewAsync();
        Assert.True(preview.CanApply);
        await catalog.ApplyAsync(preview.Fingerprint);
        var current = new TenantContext();
        current.InitializeTenant(user.Id, own.Id, [own.Id], [own.Id], own.Id);
        var db = new IdentityDbContext(options, current);
        return (db, current, ownerRole.Id, editor.Id, foreign.Id);
    }

    private static async Task WithDisposableDatabaseAsync(
        Func<IdentityDbContext, DbContextOptions<IdentityDbContext>, Task> test)
    {
        var raw = Environment.GetEnvironmentVariable("SHINERA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "SHINERA_SQLSERVER_TEST_CONNECTION is required for disposable SQL tests.");
        var source = new SqlConnectionStringBuilder(raw);
        if (!source.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            !(source.DataSource.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              source.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Disposable loopback master SQL required.");

        source.InitialCatalog = "FoundationTenantGrants_" + Guid.NewGuid().ToString("N");
        source.ConnectTimeout = 30;
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(source.ConnectionString).Options;
        await using var setup = new IdentityDbContext(options, new TenantContext());
        try
        {
            await setup.Database.MigrateAsync();
            await test(setup, options);
        }
        finally
        {
            setup.ChangeTracker.Clear();
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
