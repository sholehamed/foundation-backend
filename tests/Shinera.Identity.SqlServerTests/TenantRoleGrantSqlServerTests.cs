using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
            setup.UserRoles.Remove(await setup.UserRoles.SingleAsync(x =>
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
