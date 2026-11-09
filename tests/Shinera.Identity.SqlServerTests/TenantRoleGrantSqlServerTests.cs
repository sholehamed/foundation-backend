using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.System.Identity.Application.Services;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure;
using System.Security.Claims;
using System.Text.Json;

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
    public async Task Group_security_review_unifies_ledgers_and_traces_unattributed_legacy_links()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (db, owner, ownerRole, ordinaryRole, _) = await SeedAsync(setup, options);
            await using (db)
            {
                var tenantId = owner.ActiveTenantId!.Value;
                var ownerId = owner.UserId!.Value;
                var otherTenant = await setup.Tenants.AsNoTracking()
                    .Where(t => t.Slug == "other-role-grants")
                    .Select(t => t.Id).SingleAsync();

                var staff = new User(Guid.NewGuid(), tenantId, "group-audit-staff",
                    "group-audit-staff@example.test", "Staff", "Test", UserType.Tenant)
                {
                    NormalizedUserName = "GROUP-AUDIT-STAFF",
                    NormalizedEmail = "GROUP-AUDIT-STAFF@EXAMPLE.TEST",
                    PasswordHash = "fixture", IsActive = true
                };
                setup.Users.Add(staff);
                setup.UserRoles.Add(new UserRole(tenantId, staff.Id, ordinaryRole));
                await setup.SaveChangesAsync();
                var role = await db.Roles.AsNoTracking()
                    .SingleAsync(r => r.Id == ordinaryRole);
                await TenantRolePermissionGrantWriter.ReplaceAsync(
                    owner, db, ordinaryRole,
                    new(Convert.ToBase64String(role.RowVersion),
                    [
                        TenantWorkspacePermissionCatalog.GroupsRead,
                        TenantWorkspacePermissionCatalog.MembersRead,
                        TenantWorkspacePermissionCatalog.RolesRead
                    ]));

                var group = await TenantGroupLifecycleWriter.CreateAsync(
                    owner, db, new("Historical team", "existing roles"));
                var now = DateTime.UtcNow;

                // Existing links may predate reviewed grant APIs. Some are
                // legitimate, some unsafe; never delete them on reading.
                setup.UserGroups.AddRange(
                    new UserGroup(Guid.NewGuid())
                    { TenantId = tenantId, GroupId = group.Id, UserId = staff.Id },
                    new UserGroup(Guid.NewGuid())
                    { TenantId = tenantId, GroupId = group.Id, UserId = ownerId });
                setup.GroupRoles.AddRange(
                    new GroupRole(Guid.NewGuid())
                    { TenantId = tenantId, GroupId = group.Id, RoleId = ordinaryRole },
                    new GroupRole(Guid.NewGuid())
                    { TenantId = tenantId, GroupId = group.Id, RoleId = ownerRole });
                await setup.SaveChangesAsync();

                // Matching historical grant only for ordinary links; the
                // Owner links intentionally have NO ledger provenance.
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantGroupMembershipAuditEntries]
                        ([Id],[TenantId],[ActorUserId],[GroupId],
                         [TargetUserId],[Action],[OccurredAtUtc])
                    VALUES ({Guid.NewGuid()},{tenantId},{ownerId},{group.Id},
                        {staff.Id},{"grant"},{now.AddMinutes(2)})
                    """);
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantGroupRoleAuditEntries]
                        ([Id],[TenantId],[ActorUserId],[GroupId],
                         [RoleId],[RoleName],[Action],[OccurredAtUtc])
                    VALUES ({Guid.NewGuid()},{tenantId},{ownerId},{group.Id},
                        {ordinaryRole},{"Reception"},{"grant"},{now.AddMinutes(3)})
                    """);
                for (var i = 0; i < 22; i++)
                {
                    await setup.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO [dbo].[TenantGroupChangeAuditEntries]
                            ([Id],[TenantId],[ActorUserId],[GroupId],
                             [Action],[CurrentName],[CurrentActive],[OccurredAtUtc])
                        VALUES ({Guid.NewGuid()},{tenantId},{ownerId},{group.Id},
                            {"state"},{"Historical team"},{true},{now.AddSeconds(i)})
                        """);
                }
                // Foreign Tenant with SAME GroupId, newer timestamp. A
                // reader filtering on GroupId alone would leak this record.
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantGroupRoleAuditEntries]
                        ([Id],[TenantId],[ActorUserId],[GroupId],
                         [RoleId],[RoleName],[Action],[OccurredAtUtc])
                    VALUES ({Guid.NewGuid()},{otherTenant},{ownerId},{group.Id},
                        {ordinaryRole},{"FOREIGN ROLE LEAK"},{"grant"},{now.AddHours(1)})
                    """);

                var http = new DefaultHttpContext();
                var firstResult = await TenantWorkspace.GetGroupAudit(
                    owner, db, http, group.Id, 1);
                var first = Assert.IsType<TenantGroupSecurityAuditReader.Page>(
                    Assert.IsAssignableFrom<IValueHttpResult>(firstResult).Value);
                Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
                Assert.Equal(group.Id, first.GroupId);
                Assert.Equal("Historical team", first.GroupName);
                Assert.Equal(20, first.Items.Count);
                Assert.True(first.HasNext);
                Assert.DoesNotContain(first.Items, x => x.RoleName == "FOREIGN ROLE LEAK");
                Assert.Equal(2, first.Integrity.MemberLinks);
                Assert.Equal(2, first.Integrity.RoleLinks);
                Assert.Equal(1, first.Integrity.OwnerMemberLinks);
                Assert.Equal(1, first.Integrity.InvalidRoleLinks);
                Assert.Equal(0, first.Integrity.InvalidMemberLinks);
                Assert.Equal(1, first.Integrity.UnattributedMemberLinks);
                Assert.Equal(1, first.Integrity.UnattributedRoleLinks);

                var second = Assert.IsType<TenantGroupSecurityAuditReader.Page>(
                    Assert.IsAssignableFrom<IValueHttpResult>(
                        await TenantWorkspace.GetGroupAudit(
                            owner, db, new DefaultHttpContext(), group.Id, 2)).Value);
                Assert.Equal(5, second.Items.Count);
                Assert.False(second.HasNext);
                Assert.Contains(second.Items, x => x.Category == "group_member" &&
                    x.TargetUserId == staff.Id && x.TargetUserName == "group-audit-staff");
                Assert.Contains(second.Items, x => x.Category == "group_role" &&
                    x.RoleId == ordinaryRole && x.RoleName == "Reception");
                Assert.All(first.Items.Concat(second.Items), x =>
                    Assert.NotEqual("FOREIGN ROLE LEAK", x.RoleName));
                Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                    await TenantWorkspace.GetGroupAudit(
                        owner, db, new DefaultHttpContext(), group.Id, 0)).StatusCode);
                Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                    await TenantWorkspace.GetGroupAudit(
                        owner, db, new DefaultHttpContext(), Guid.NewGuid(), 1)).StatusCode);

                // Staff has all three read PAGE permissions, but NOT Owner.
                var staffTenant = new TenantContext();
                staffTenant.InitializeTenant(staff.Id, tenantId,
                    [tenantId], [tenantId], tenantId);
                await using var staffDb = new IdentityDbContext(options, staffTenant);
                foreach (var key in new[] {
                    TenantWorkspacePermissionCatalog.GroupsRead,
                    TenantWorkspacePermissionCatalog.MembersRead,
                    TenantWorkspacePermissionCatalog.RolesRead
                })
                    Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                        staffTenant, staffDb, key, default));
                Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                    await TenantWorkspace.GetGroupAudit(
                        staffTenant, staffDb, new DefaultHttpContext(), group.Id)).StatusCode);
                Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                    await TenantWorkspace.GetGroupAudit(
                        new TenantContext(), setup, new DefaultHttpContext(), group.Id)).StatusCode);

                // Inspection MUST NOT mutate either legacy relationship.
                Assert.Equal(2, await setup.UserGroups
                    .IgnoreQueryFilters(["tenant"])
                    .CountAsync(x => x.TenantId == tenantId && x.GroupId == group.Id));
                Assert.Equal(2, await setup.GroupRoles
                    .IgnoreQueryFilters(["tenant"])
                    .CountAsync(x => x.TenantId == tenantId && x.GroupId == group.Id));
            }
        });
    }

    [Fact]
    public async Task Member_audit_is_owner_only_bounded_and_cannot_cross_tenants()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (db, owner, _, ordinaryRoleId, _) = await SeedAsync(setup, options);
            await using (db)
            {
                var tenantId = owner.ActiveTenantId!.Value;
                var ownerId = owner.UserId!.Value;
                var foreignTenantId = await setup.Tenants.AsNoTracking()
                    .Where(x => x.Slug == "other-role-grants").Select(x => x.Id).SingleAsync();
                var now = DateTime.UtcNow;

                // Use real migrated, append-only SQL ledgers; deliberately
                // inject one foreign row with the SAME target user ID.
                for (var i = 0; i < 22; i++)
                {
                    await setup.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                            ([Id],[TenantId],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
                        VALUES ({Guid.NewGuid()},{tenantId},{ownerId},{ownerId},
                            {"create_inactive"},{now.AddSeconds(i)})
                        """);
                }
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantMemberRoleAuditEntries]
                        ([Id],[TenantId],[ActorUserId],[TargetUserId],
                         [RoleId],[RoleName],[Action],[OccurredAtUtc])
                    VALUES ({Guid.NewGuid()},{tenantId},{ownerId},{ownerId},
                        {ordinaryRoleId},{"Reception"},{"grant"},{now.AddMinutes(5)})
                    """);
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[TenantMemberLifecycleAuditEntries]
                        ([Id],[TenantId],[ActorUserId],[TargetUserId],[Action],[OccurredAtUtc])
                    VALUES ({Guid.NewGuid()},{foreignTenantId},{ownerId},{ownerId},
                        {"invite_failed"},{now.AddMinutes(10)})
                    """);

                var context = new DefaultHttpContext();
                var first = await TenantWorkspace.GetMemberAudit(owner, db, context, 1);
                var result = Assert.IsAssignableFrom<IValueHttpResult>(first);
                var page = Assert.IsType<TenantMemberAuditReader.Page>(result.Value);
                Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
                Assert.Equal(20, page.Items.Count);
                Assert.True(page.HasNext);
                Assert.Equal("member_role", page.Items[0].Category);
                Assert.Equal("grant", page.Items[0].Action);
                Assert.Equal("Reception", page.Items[0].RoleName);
                Assert.DoesNotContain(page.Items, x => x.Action == "invite_failed");
                Assert.All(page.Items, x => Assert.Equal(ownerId, x.TargetUserId));

                var second = await TenantWorkspace.GetMemberAudit(
                    owner, db, new DefaultHttpContext(), 2, ownerId);
                var last = Assert.IsType<TenantMemberAuditReader.Page>(
                    Assert.IsAssignableFrom<IValueHttpResult>(second).Value);
                Assert.Equal(3, last.Items.Count);
                Assert.False(last.HasNext);
                var otherTarget = await TenantWorkspace.GetMemberAudit(
                    owner, db, new DefaultHttpContext(), 1, Guid.NewGuid());
                Assert.Empty(Assert.IsType<TenantMemberAuditReader.Page>(
                    Assert.IsAssignableFrom<IValueHttpResult>(otherTarget).Value).Items);
                Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                    await TenantWorkspace.GetMemberAudit(
                        owner, db, new DefaultHttpContext(), 0)).StatusCode);

                // Real ordinary staff is allowed MEMBERS.READ through its
                // role, but must still be forbidden from Owner security audit.
                var staff = new User(tenantId, "audit-staff",
                    "audit-staff@example.test", "Audit", "Staff", UserType.Tenant)
                {
                    IsActive = true, NormalizedUserName = "AUDIT-STAFF",
                    NormalizedEmail = "AUDIT-STAFF@EXAMPLE.TEST", PasswordHash = "fixture"
                };
                setup.Users.Add(staff);
                setup.UserRoles.Add(new UserRole(tenantId, staff.Id, ordinaryRoleId));
                await setup.SaveChangesAsync();
                var readPermission = await setup.Permissions.SingleAsync(
                    x => x.Code == TenantWorkspacePermissionCatalog.MembersRead);
                // The role's permission is applied via the existing audited Owner CAS.
                var version = Convert.ToBase64String(
                    (await db.Roles.AsNoTracking().SingleAsync(
                        x => x.Id == ordinaryRoleId)).RowVersion);
                await TenantRolePermissionGrantWriter.ReplaceAsync(
                    owner, db, ordinaryRoleId,
                    new TenantRolePermissionGrantWriter.ReplaceRequest(
                        version, [readPermission.Code]));

                var staffContext = new TenantContext();
                staffContext.InitializeTenant(staff.Id, tenantId,
                    [tenantId], [tenantId], tenantId);
                await using var staffDb = new IdentityDbContext(options, staffContext);
                Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffContext, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
                var denied = await TenantWorkspace.GetMemberAudit(
                    staffContext, staffDb, new DefaultHttpContext());
                Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
            }
        });
    }

    [Fact]
    public async Task Group_role_grant_is_owner_only_scoped_audited_and_live_revocable()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, owner, ownerRoleId, roleId, foreignRoleId) =
                await SeedAsync(setup, options);
            await using (owned)
            {
                var tenantId = owner.ActiveTenantId!.Value;
                var staff = new User(Guid.NewGuid(), tenantId, "group-role-staff",
                    "group-role-staff@example.test", "Staff", "Test", UserType.Tenant)
                {
                    IsActive = true, NormalizedUserName = "GROUP-ROLE-STAFF",
                    NormalizedEmail = "GROUP-ROLE-STAFF@EXAMPLE.TEST",
                    PasswordHash = "fixture"
                };
                setup.Users.Add(staff);
                await setup.SaveChangesAsync();
                var group = await TenantGroupLifecycleWriter.CreateAsync(
                    owner, owned, new("Front Desk Roles", null));
                // Legitimate membership is already supported by FR-13C-2B;
                // this test starts with a member but NO GroupRole.
                setup.UserGroups.Add(new UserGroup(Guid.NewGuid())
                {
                    TenantId = tenantId, GroupId = group.Id, UserId = staff.Id
                });
                var permission = await setup.Permissions.SingleAsync(x =>
                    x.Code == TenantWorkspacePermissionCatalog.MembersRead);
                setup.RolePermissions.Add(new RolePermission(tenantId, roleId, permission.Id));
                await setup.SaveChangesAsync();

                var trusted = new TrustedTenantWriteScope(
                    NullLogger<TrustedTenantWriteScope>.Instance);
                var protectedOptions = new DbContextOptionsBuilder<IdentityDbContext>(options)
                    .AddInterceptors(new TenantSaveChangesInterceptor(owner, trusted)).Options;
                await using var db = new IdentityDbContext(protectedOptions, owner);
                var staffTenant = new TenantContext();
                staffTenant.InitializeTenant(staff.Id, tenantId,
                    [tenantId], [], tenantId);
                await using var staffDb = new IdentityDbContext(options, staffTenant);
                Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffTenant, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

                // Tenant write scope is not grant authority.
                db.GroupRoles.Add(new GroupRole(Guid.NewGuid())
                {
                    TenantId = tenantId, GroupId = group.Id, RoleId = roleId
                });
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                var before = await TenantGroupRoleAssignmentWriter.ReadAsync(owner, db, group.Id);
                Assert.NotNull(before);
                Assert.True(before.CanEdit);
                Assert.Contains(before.Items,
                    r => r.Id == roleId && !r.Assigned);
                Assert.DoesNotContain(before.Items, r => r.Id == ownerRoleId);

                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, ownerRoleId,
                        new(true, before.Version), trusted));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, foreignRoleId,
                        new(true, before.Version), trusted));

                var granted = await TenantGroupRoleAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, roleId, new(true, before.Version), trusted);
                Assert.NotEqual(before.Version, granted);
                Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffTenant, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
                await Assert.ThrowsAsync<TenantGroupRoleConflictException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, roleId,
                        new(false, before.Version), trusted));
                Assert.Equal(granted, await TenantGroupRoleAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, roleId, new(true, granted), trusted));

                var revoked = await TenantGroupRoleAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, roleId, new(false, granted), trusted);
                Assert.NotEqual(granted, revoked);
                Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffTenant, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

                var events = await db.Database.SqlQueryRaw<string>(
                    "SELECT [Action] AS [Value] FROM [dbo].[TenantGroupRoleAuditEntries]")
                    .ToArrayAsync();
                Assert.Equal(2, events.Length);
                Assert.Contains("grant", events);
                Assert.Contains("revoke", events);
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [dbo].[TenantGroupRoleAuditEntries]"));

                // Even an Owner must never assign a role to a Group containing
                // themselves; the role mutation cannot be self-elevation.
                setup.UserGroups.Add(new UserGroup(Guid.NewGuid())
                {
                    TenantId = tenantId, GroupId = group.Id,
                    UserId = owner.UserId!.Value
                });
                await setup.SaveChangesAsync();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, roleId, new(true, revoked), trusted));
                setup.UserGroups.Remove(await setup.UserGroups
                    .IgnoreQueryFilters(["tenant"]).SingleAsync(x =>
                        x.TenantId == tenantId && x.GroupId == group.Id &&
                        x.UserId == owner.UserId));
                await setup.SaveChangesAsync();

                // Disabling a Group or Role cannot be bypassed by a stale UI.
                var old = await db.Groups.AsNoTracking()
                    .SingleAsync(x => x.Id == group.Id);
                var inactive = await TenantGroupLifecycleWriter.ChangeStateAsync(
                    owner, owned, group.Id, new(false,
                        Convert.ToBase64String(old.RowVersion)));
                await Assert.ThrowsAsync<TenantGroupRoleConflictException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, roleId, new(true, inactive.Version), trusted));
                var enabled = await TenantGroupLifecycleWriter.ChangeStateAsync(
                    owner, owned, group.Id, new(true, inactive.Version));

                // Missing audit rolls back both relationship and Group CAS.
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TABLE [dbo].[TenantGroupRoleAuditEntries]");
                await Assert.ThrowsAsync<SqlException>(() =>
                    TenantGroupRoleAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, roleId, new(true, enabled.Version), trusted));
                db.ChangeTracker.Clear();
                Assert.False(await db.GroupRoles.AsNoTracking().AnyAsync(gr =>
                    gr.TenantId == tenantId && gr.GroupId == group.Id && gr.RoleId == roleId));
                Assert.Equal(enabled.Version, Convert.ToBase64String(
                    (await db.Groups.AsNoTracking().SingleAsync(
                        g => g.Id == group.Id)).RowVersion));
            }
        });
    }

    [Fact]
    public async Task Group_membership_requires_scoped_intent_owner_CAS_and_audit_and_revokes_live_grants()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, owner, _, roleId, _) = await SeedAsync(setup, options);
            await using (owned)
            {
                var tenantId = owner.ActiveTenantId!.Value;
                var staff = new User(Guid.NewGuid(), tenantId, "group-member-a",
                    "group-member-a@example.test", "Member", "A", UserType.Tenant)
                {
                    IsActive = true, NormalizedUserName = "GROUP-MEMBER-A",
                    NormalizedEmail = "GROUP-MEMBER-A@EXAMPLE.TEST", PasswordHash = "fixture"
                };
                setup.Users.Add(staff);
                await setup.SaveChangesAsync();
                var group = await TenantGroupLifecycleWriter.CreateAsync(
                    owner, owned, new("E2E Shared Support", null));
                var permission = await setup.Permissions.SingleAsync(p =>
                    p.Code == TenantWorkspacePermissionCatalog.MembersRead);
                // Existing production data may already have GroupRole links.
                // Explicitly seed one for the disposable test, not through an
                // unauthorized Tenant GroupRole grant endpoint.
                setup.GroupRoles.Add(new GroupRole(Guid.NewGuid())
                {
                    TenantId = tenantId, GroupId = group.Id, RoleId = roleId
                });
                setup.RolePermissions.Add(new RolePermission(tenantId, roleId, permission.Id));
                await setup.SaveChangesAsync();

                var trusted = new TrustedTenantWriteScope(
                    NullLogger<TrustedTenantWriteScope>.Instance);
                var protectedOptions = new DbContextOptionsBuilder<IdentityDbContext>(options)
                    .AddInterceptors(new TenantSaveChangesInterceptor(owner, trusted)).Options;
                await using var db = new IdentityDbContext(protectedOptions, owner);
                // Ordinary WritableTenantIds alone MUST fail to write UserGroup.
                db.UserGroups.Add(new UserGroup(Guid.NewGuid())
                { TenantId = tenantId, GroupId = group.Id, UserId = staff.Id });
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                var staffContext = new TenantContext();
                staffContext.InitializeTenant(staff.Id, tenantId,
                    [tenantId], [], tenantId);
                await using var staffDb = new IdentityDbContext(options, staffContext);
                Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffContext, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));

                var before = await TenantGroupMemberAssignmentWriter.ReadAsync(
                    owner, db, group.Id);
                Assert.NotNull(before);
                Assert.True(before.CanEdit);
                Assert.Contains(before.Items, x => x.Id == staff.Id && !x.Assigned);

                var version = await TenantGroupMemberAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, staff.Id, new(true, before.Version), trusted);
                Assert.NotEqual(before.Version, version);
                Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffContext, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
                Assert.True(await db.UserGroups.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.GroupId == group.Id && x.UserId == staff.Id));
                await Assert.ThrowsAsync<TenantGroupMemberConflictException>(() =>
                    TenantGroupMemberAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, staff.Id, new(false, before.Version), trusted));
                Assert.Equal(version, await TenantGroupMemberAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, staff.Id, new(true, version), trusted));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupMemberAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, owner.UserId!.Value,
                        new(true, version), trusted));

                var removed = await TenantGroupMemberAssignmentWriter.ChangeAsync(
                    owner, db, group.Id, staff.Id, new(false, version), trusted);
                Assert.NotEqual(version, removed);
                Assert.False(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffContext, staffDb, TenantWorkspacePermissionCatalog.MembersRead, default));
                Assert.False(await db.UserGroups.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.GroupId == group.Id && x.UserId == staff.Id));

                var audit = await db.Database.SqlQueryRaw<string>(
                    "SELECT [Action] AS [Value] FROM [dbo].[TenantGroupMembershipAuditEntries]")
                    .ToArrayAsync();
                Assert.Equal(2, audit.Length);
                Assert.Contains("grant", audit);
                Assert.Contains("revoke", audit);
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [dbo].[TenantGroupMembershipAuditEntries]"));

                // No write to foreign Tenant, even with a guessed GUID.
                var foreignGroup = new Group(Guid.NewGuid())
                {
                    TenantId = (await setup.Tenants.AsNoTracking()
                        .SingleAsync(x => x.Slug == "other-role-grants")).Id,
                    Name = "Foreign Group", NormalizedName = "FOREIGN GROUP"
                };
                setup.Groups.Add(foreignGroup);
                await setup.SaveChangesAsync();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupMemberAssignmentWriter.ChangeAsync(
                        owner, db, foreignGroup.Id, staff.Id, new(true, removed), trusted));

                // Missing ledger must roll back the UserGroup and Group CAS.
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TABLE [dbo].[TenantGroupMembershipAuditEntries]");
                await Assert.ThrowsAsync<SqlException>(() =>
                    TenantGroupMemberAssignmentWriter.ChangeAsync(
                        owner, db, group.Id, staff.Id, new(true, removed), trusted));
                db.ChangeTracker.Clear();
                Assert.False(await db.UserGroups.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.GroupId == group.Id && x.UserId == staff.Id));
                var afterFailure = (await db.Groups.AsNoTracking()
                    .SingleAsync(x => x.Id == group.Id)).RowVersion;
                Assert.Equal(removed, Convert.ToBase64String(afterFailure));
            }
        });
    }

    [Fact]
    public async Task Tenant_group_lifecycle_is_owner_only_CAS_scoped_and_append_only()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (db, owner, _, ordinaryRole, _) = await SeedAsync(setup, options);
            await using (db)
            {
                var tenantId = owner.ActiveTenantId!.Value;
                var group = await TenantGroupLifecycleWriter.CreateAsync(
                    owner, db, new("  Reception Team  ", "  Customer service  "));
                Assert.Equal("Reception Team", group.Name);
                Assert.Equal("Customer service", group.Description);
                Assert.True(group.IsActive);
                Assert.NotEmpty(group.Version);
                Assert.False(await db.UserGroups.AsNoTracking().AnyAsync(
                    x => x.GroupId == group.Id));
                Assert.False(await db.GroupRoles.AsNoTracking().AnyAsync(
                    x => x.GroupId == group.Id));

                await Assert.ThrowsAsync<TenantGroupLifecycleConflictException>(() =>
                    TenantGroupLifecycleWriter.CreateAsync(
                        owner, db, new("RECEPTION TEAM", null)));
                await Assert.ThrowsAsync<TenantGroupLifecycleValidationException>(() =>
                    TenantGroupLifecycleWriter.CreateAsync(
                        owner, db, new("OWNER", null)));

                var renamed = await TenantGroupLifecycleWriter.UpdateAsync(
                    owner, db, group.Id, new("Front Desk Team", "Public lobby", group.Version));
                Assert.NotEqual(group.Version, renamed.Version);
                Assert.Equal("Front Desk Team", renamed.Name);
                await Assert.ThrowsAsync<TenantGroupLifecycleConflictException>(() =>
                    TenantGroupLifecycleWriter.UpdateAsync(
                        owner, db, group.Id, new("Stale group", null, group.Version)));
                var noOp = await TenantGroupLifecycleWriter.UpdateAsync(
                    owner, db, group.Id, new(renamed.Name, renamed.Description, renamed.Version));
                Assert.Equal(renamed.Version, noOp.Version);

                var disabled = await TenantGroupLifecycleWriter.ChangeStateAsync(
                    owner, db, group.Id, new(false, renamed.Version));
                Assert.False(disabled.IsActive);
                await Assert.ThrowsAsync<TenantGroupLifecycleConflictException>(() =>
                    TenantGroupLifecycleWriter.ChangeStateAsync(
                        owner, db, group.Id, new(true, renamed.Version)));
                var enabled = await TenantGroupLifecycleWriter.ChangeStateAsync(
                    owner, db, group.Id, new(true, disabled.Version));
                Assert.True(enabled.IsActive);

                Assert.Equal(4, await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantGroupChangeAuditEntries]")
                    .SingleAsync());
                var events = await db.Database.SqlQueryRaw<string>(
                    "SELECT [Action] AS [Value] FROM [dbo].[TenantGroupChangeAuditEntries]")
                    .ToArrayAsync();
                Assert.Contains("create", events);
                Assert.Contains("update", events);
                Assert.Equal(2, events.Count(x => x == "state"));
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [dbo].[TenantGroupChangeAuditEntries]"));

                // A user granted read via a Tenant role must not mutate Groups.
                var user = new User(Guid.NewGuid(), tenantId,
                    "group-staff", "group-staff@example.test", "Group", "Staff", UserType.Tenant)
                {
                    IsActive = true, NormalizedUserName = "GROUP-STAFF",
                    NormalizedEmail = "GROUP-STAFF@EXAMPLE.TEST", PasswordHash = "fixture"
                };
                setup.Users.Add(user);
                setup.UserRoles.Add(new UserRole(tenantId, user.Id, ordinaryRole));
                await setup.SaveChangesAsync();
                var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Id == ordinaryRole);
                await TenantRolePermissionGrantWriter.ReplaceAsync(
                    owner, db, ordinaryRole,
                    new(Convert.ToBase64String(role.RowVersion),
                        [TenantWorkspacePermissionCatalog.GroupsRead]));
                var staffContext = new TenantContext();
                staffContext.InitializeTenant(user.Id, tenantId,
                    [tenantId], [tenantId], tenantId);
                await using var staffDb = new IdentityDbContext(options, staffContext);
                Assert.True(await TenantWorkspacePermissionEvaluator.AllowsAsync(
                    staffContext, staffDb, TenantWorkspacePermissionCatalog.GroupsRead, default));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupLifecycleWriter.CreateAsync(
                        staffContext, staffDb, new("Unauthorized group", null)));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupLifecycleWriter.ChangeStateAsync(
                        staffContext, staffDb, group.Id, new(false, enabled.Version)));

                var foreignTenant = await setup.Tenants.AsNoTracking()
                    .Where(x => x.Slug == "other-role-grants")
                    .Select(x => x.Id).SingleAsync();
                var foreignGroup = new Group(Guid.NewGuid())
                {
                    TenantId = foreignTenant, Name = "Other workspace group",
                    NormalizedName = "OTHER WORKSPACE GROUP", IsActive = true
                };
                setup.Groups.Add(foreignGroup);
                await setup.SaveChangesAsync();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    TenantGroupLifecycleWriter.UpdateAsync(
                        owner, db, foreignGroup.Id, new("Hijack", null,
                            Convert.ToBase64String(foreignGroup.RowVersion))));
            }
        });
    }

    [Fact]
    public async Task Group_create_rollback_when_append_only_audit_cannot_be_written()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (db, owner, _, _, _) = await SeedAsync(setup, options);
            await using (db)
            {
                await setup.Database.ExecuteSqlRawAsync(
                    "DROP TABLE [dbo].[TenantGroupChangeAuditEntries]");
                await Assert.ThrowsAsync<SqlException>(() =>
                    TenantGroupLifecycleWriter.CreateAsync(
                        owner, db, new("Rollback group", "Must never persist")));
                db.ChangeTracker.Clear();
                Assert.False(await db.Groups.AsNoTracking()
                    .AnyAsync(x => x.Name == "Rollback group"));
            }
        });
    }

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

    [Fact]
    public async Task Expired_invitation_cannot_activate_even_with_correct_email_token()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "expire.staff", "expire.staff@example.test", "Expire", "Staff"));
            var sender = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, sender, member.Id);
            // Move BOTH clocks so the SQL ExpiresAtUtc > CreatedAtUtc
            // constraint remains valid but the token is now expired.
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE [dbo].[TenantMemberInvitations]
                SET [CreatedAtUtc]=DATEADD(HOUR,-2,[CreatedAtUtc]),
                    [ExpiresAtUtc]=DATEADD(HOUR,-2,[ExpiresAtUtc])
                """);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    new TenantMemberInvitationWriter.ActivateRequest(
                        "tenant-role-grants", sender.Token, "P@ssword-For-St4ff!")));
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
        });
    }

    [Fact]
    public async Task Explicit_Owner_deny_or_inactive_invite_ACTION_blocks_dispatch()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "deny.invite", "deny.invite@example.test", "Deny", "Invite"));
            var invitePermission = await setup.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.MembersInvite);
            setup.UserPermissions.Add(new UserPermission
            {
                TenantId = actor.ActiveTenantId!.Value, UserId = actor.UserId!.Value,
                PermissionId = invitePermission.Id, IsGranted = false
            });
            await setup.SaveChangesAsync();
            Assert.False(await TenantMemberInvitationWriter.CanInviteAsync(actor, db, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db,
                    new FakeInvitationSender(), member.Id));
            setup.UserPermissions.Remove(await setup.UserPermissions.IgnoreQueryFilters(["tenant"]).SingleAsync(x =>
                x.TenantId == actor.ActiveTenantId && x.UserId == actor.UserId &&
                x.PermissionId == invitePermission.Id));
            var ui = await setup.UiResources.SingleAsync(x =>
                x.Key == "tenant.workspace.members.invite");
            ui.IsActive = false;
            await setup.SaveChangesAsync();
            Assert.False(await TenantMemberInvitationWriter.CanInviteAsync(actor, db, default));
        });
    }

    [Fact]
    public async Task Invitation_does_not_activate_if_registered_member_email_changed()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "email.swap", "original@example.test", "Email", "Change"));
            var sender = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, sender, member.Id);

            var changed = await setup.Users.SingleAsync(x => x.Id == member.Id);
            changed.Email = "new-owner@example.test";
            changed.NormalizedEmail = "NEW-OWNER@EXAMPLE.TEST";
            await setup.SaveChangesAsync();

            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
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

    [Fact]
    public async Task First_login_requires_password_change_and_new_full_login()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "first.staff", "first.staff@example.test", "First", "Staff"));
            Assert.True(await TenantFirstLoginWriter.CanIssueAsync(actor, db, default));
            var issued = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            Assert.Equal(27, issued.TemporaryPassword.Length);
            Assert.True(issued.ExpiresAtUtc > DateTime.UtcNow);
            Assert.False((await db.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
            Assert.False(await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberTemporaryCredentials] WHERE [PasswordHash] LIKE '%first.staff%'")
                .SingleAsync() > 0);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var start = await TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "first.staff", issued.TemporaryPassword));
            Assert.True(start.RequiresPasswordChange);
            Assert.Equal(64, start.Challenge.Length);
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);

            var complete = new TenantFirstLoginWriter.CompleteRequest(
                "tenant-role-grants", start.Challenge,
                "New-Permanent-Strong-Passphrase-2026!");
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.CompleteAsync(anonymous, hasher,
                    complete with { TenantSlug = "other-role-grants" }));
            await TenantFirstLoginWriter.CompleteAsync(anonymous, hasher, complete);
            anonymous.ChangeTracker.Clear();
            var active = await anonymous.Users.AsNoTracking().SingleAsync(x => x.Id == member.Id);
            Assert.True(active.IsActive);
            Assert.Equal(PasswordVerificationResult.Success,
                hasher.VerifyHashedPassword(active, active.PasswordHash,
                    "New-Permanent-Strong-Passphrase-2026!"));
            Assert.Equal(PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(active, active.PasswordHash,
                    issued.TemporaryPassword));
            Assert.False(await anonymous.UserRoles.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.UserId == member.Id));
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.CompleteAsync(anonymous, hasher, complete));
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.StartRequest(
                        "tenant-role-grants", "first.staff", issued.TemporaryPassword)));
            var audit = await anonymous.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantMemberLifecycleAuditEntries]")
                .ToArrayAsync();
            Assert.Contains("temp_issued", audit);
            Assert.Contains("temp_verified", audit);
            Assert.Contains("temp_activated", audit);
        });
    }

    [Fact]
    public async Task Reissued_or_expired_temporary_password_revokes_prior_credentials_and_challenges()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "reissue.staff", "reissue.staff@example.test", "Reissue", "Staff"));
            var first = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var oldChallenge = await TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "reissue.staff", first.TemporaryPassword));
            var second = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            Assert.NotEqual(first.TemporaryPassword, second.TemporaryPassword);
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.StartRequest(
                        "tenant-role-grants", "reissue.staff", first.TemporaryPassword)));
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.CompleteAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.CompleteRequest(
                        "tenant-role-grants", oldChallenge.Challenge,
                        "My-Other-Strong-Passphrase-2026!")));

            // Expire the replacement without waiting a real hour.
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE [dbo].[TenantMemberTemporaryCredentials]
                SET [ExpiresAtUtc]=DATEADD(HOUR,-2,[ExpiresAtUtc]),
                    [CreatedAtUtc]=DATEADD(HOUR,-2,[CreatedAtUtc])
                WHERE [RevokedAtUtc] IS NULL AND [UsedAtUtc] IS NULL
                """);
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.StartRequest(
                        "tenant-role-grants", "reissue.staff", second.TemporaryPassword)));
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
        });
    }

    [Fact]
    public async Task Wrong_initial_password_is_throttled_and_never_issues_challenge()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "throttle.staff", "throttle.staff@example.test", "Throttle", "Staff"));
            var issued = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            for (var attempt = 0; attempt < 5; attempt++)
                await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                    TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                        new TenantFirstLoginWriter.StartRequest(
                            "tenant-role-grants", "throttle.staff",
                            "WRONG-TEMP-PASSWORD-00000000")));
            Assert.Equal(5, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT MAX([FailedAttempts]) AS [Value] FROM [dbo].[TenantMemberTemporaryCredentials]")
                .SingleAsync());
            await Assert.ThrowsAsync<FirstLoginValidationException>(() =>
                TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.StartRequest(
                        "tenant-role-grants", "throttle.staff", issued.TemporaryPassword)));
            Assert.Equal(0, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberFirstLoginChallenges]")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Failed_activation_audit_rolls_back_user_and_challenge()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "rollback.first", "rollback.first@example.test", "Rollback", "First"));
            var issued = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var start = await TenantFirstLoginWriter.StartAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "rollback.first", issued.TemporaryPassword));
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberLifecycleAuditEntries]");
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantFirstLoginWriter.CompleteAsync(anonymous, hasher,
                    new TenantFirstLoginWriter.CompleteRequest(
                        "tenant-role-grants", start.Challenge,
                        "Valid-Permanent-Strong-Passphrase-2026!")));
            anonymous.ChangeTracker.Clear();
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
            Assert.Equal(0, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberFirstLoginChallenges] WHERE [UsedAtUtc] IS NOT NULL")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Temporary_password_issue_rejects_nonowner_foreign_owner_and_direct_deny()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "scoped.first", "scoped.first@example.test", "Scoped", "First"));
            await Assert.ThrowsAsync<FirstLoginTargetException>(() =>
                TenantFirstLoginWriter.IssueAsync(actor, db, hasher, actor.UserId!.Value));
            await Assert.ThrowsAsync<FirstLoginTargetException>(() =>
                TenantFirstLoginWriter.IssueAsync(actor, db, hasher, Guid.NewGuid()));
            var permission = await setup.Permissions.SingleAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.MembersIssueTempPassword);
            setup.UserPermissions.Add(new UserPermission
            {
                TenantId = actor.ActiveTenantId!.Value,
                UserId = actor.UserId!.Value,
                PermissionId = permission.Id, IsGranted = false
            });
            await setup.SaveChangesAsync();
            Assert.False(await TenantFirstLoginWriter.CanIssueAsync(actor, db, default));
            await Assert.ThrowsAsync<FirstLoginDeniedException>(() =>
                TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id));
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberTemporaryCredentials]")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task One_step_Staff_creation_is_inactive_and_initial_password_requires_forced_change()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var output = await TenantPendingMemberWriter.CreateWithInitialPasswordAsync(
                actor, db, hasher, new TenantPendingMemberWriter.CreateRequest(
                    "one.step", "one.step@example.test", "One", "Step"));
            Assert.False(output.Member.IsActive);
            Assert.Equal(27, output.TemporaryPassword.Length);
            Assert.True(output.ExpiresAtUtc > DateTime.UtcNow);
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == output.Member.Id);
            Assert.False(user.IsActive);
            Assert.Equal(PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(user, user.PasswordHash, output.TemporaryPassword));
            Assert.False(await db.UserRoles.AsNoTracking().AnyAsync(x => x.UserId == user.Id));
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberTemporaryCredentials] WHERE [TargetUserId]={0}",
                user.Id).SingleAsync());
            var audits = await db.Database.SqlQueryRaw<string>(
                "SELECT [Action] AS [Value] FROM [dbo].[TenantMemberLifecycleAuditEntries] WHERE [TargetUserId]={0}",
                user.Id).ToArrayAsync();
            Assert.Contains("create_inactive", audits);
            Assert.Contains("temp_issued", audits);

            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var resolved = await TenantFirstLoginWriter.ResolveForLoginAsync(
                anonymous, hasher, new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "one.step", output.TemporaryPassword));
            Assert.Equal("password_change_required", resolved.Status);
            Assert.NotNull(resolved.Challenge);
            await TenantFirstLoginWriter.CompleteAsync(anonymous, hasher,
                new TenantFirstLoginWriter.CompleteRequest(
                    "tenant-role-grants", resolved.Challenge,
                    "Fresh-Long-Password-For-Employee-2026!"));
            anonymous.ChangeTracker.Clear();
            Assert.True((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == user.Id)).IsActive);
        });
    }

    [Fact]
    public async Task One_step_Staff_creation_rolls_back_both_user_and_password_when_audit_fails()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            await setup.Database.ExecuteSqlRawAsync(
                "DROP TABLE [dbo].[TenantMemberLifecycleAuditEntries]");
            await Assert.ThrowsAsync<SqlException>(() =>
                TenantPendingMemberWriter.CreateWithInitialPasswordAsync(
                    actor, db, new PasswordHasher<User>(),
                    new TenantPendingMemberWriter.CreateRequest(
                        "atomic.step", "atomic.step@example.test", "Atomic", "Step")));
            db.ChangeTracker.Clear();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(x =>
                x.NormalizedUserName == "ATOMIC.STEP" &&
                x.TenantId == actor.ActiveTenantId));
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberTemporaryCredentials]")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Unified_Tenant_login_decision_preserves_normal_auth_and_limits_temp_password_to_change_challenge()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "unified.staff", "unified.staff@example.test", "Unified", "Staff"));
            var issued = await TenantFirstLoginWriter.IssueAsync(actor, db, hasher, member.Id);
            await using var anonymous = new IdentityDbContext(options, new TenantContext());

            var ordinary = await TenantFirstLoginWriter.ResolveForLoginAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "unified.staff", "Ordinary-Password-Only"));
            Assert.Equal("continue_login", ordinary.Status);
            Assert.Null(ordinary.Challenge);

            var unknown = await TenantFirstLoginWriter.ResolveForLoginAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "not-registered",
                    "ABCDEF-GHJKLM-NPQRST-UVWXYZ"));
            Assert.Equal("continue_login", unknown.Status);
            Assert.Null(unknown.Challenge);

            var wrong = await TenantFirstLoginWriter.ResolveForLoginAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "unified.staff",
                    "ABCDEF-GHJKLM-NPQRST-UVWXYZ"));
            Assert.Equal("continue_login", wrong.Status);
            Assert.Null(wrong.Challenge);

            var restricted = await TenantFirstLoginWriter.ResolveForLoginAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "unified.staff", issued.TemporaryPassword));
            Assert.Equal("password_change_required", restricted.Status);
            Assert.Equal(64, restricted.Challenge?.Length);
            Assert.NotNull(restricted.ExpiresAtUtc);
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id)).IsActive);
            await TenantFirstLoginWriter.CompleteAsync(anonymous, hasher,
                new TenantFirstLoginWriter.CompleteRequest(
                    "tenant-role-grants", restricted.Challenge,
                    "New-Permanent-Strong-Passphrase-2026!"));
            anonymous.ChangeTracker.Clear();
            var activated = await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == member.Id);
            Assert.True(activated.IsActive);
            var after = await TenantFirstLoginWriter.ResolveForLoginAsync(anonymous, hasher,
                new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "unified.staff", issued.TemporaryPassword));
            Assert.Equal("continue_login", after.Status);
            Assert.Null(after.Challenge);
        });
    }


    [Fact]
    public async Task Identity_host_interactive_login_forces_change_without_signing_in_then_requires_new_login()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var pending = await TenantPendingMemberWriter.CreateWithInitialPasswordAsync(
                actor, db, hasher, new TenantPendingMemberWriter.CreateRequest(
                    "idp.member", "idp.member@example.test", "Idp", "Member"));
            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Identity:TenantFirstLogin:Enabled"] = "true",
                    ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
                    ["Identity:Bff:Authority"] = "https://identity.shinera.localhost",
                    ["Identity:E2E:Enabled"] = "true",
                    ["Identity:E2E:CaptchaCode"] = "ABCDE",
                    ["Identity:AllowInsecureHttpForE2E"] = "true",
                }).Build();
            var captcha = new CaptchaService(
                DataProtectionProvider.Create("FoundationFirstLoginHttpTest"),
                config, new FirstLoginTestEnvironment());
            var captchaId = captcha.CreateChallenge().Token;
            var session = new RecordingAuthenticationService();
            using var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IdentityDbContext>(anonymous)
                .AddSingleton<IPasswordHasher<User>>(hasher)
                .AddSingleton<IAuthenticationService>(session)
                .BuildServiceProvider();
            static DefaultHttpContext NewRequest(IServiceProvider services)
            {
                var ctx = new DefaultHttpContext { RequestServices = services };
                ctx.Request.Scheme = "https";
                ctx.Request.Host = new HostString("identity.shinera.localhost");
                return ctx;
            }

            var endpoint = new Auth();
            var auth = new UserCredentialAuthenticator(anonymous, hasher,
                new IdentityPrincipalFactory(anonymous, new DefaultSystemTenantScope()));
            var input = new InteractiveLoginRequest(
                "idp.member", pending.TemporaryPassword, captchaId,
                "ABCDE", "tenant", "tenant-role-grants");
            var anti = new PassThroughAntiforgery();

            var csrfFailure = await endpoint.InteractiveLogin(
                input, NewRequest(services), new PassThroughAntiforgery(reject: true),
                captcha, auth, default);
            Assert.Equal(400, ((IStatusCodeHttpResult)csrfFailure).StatusCode);
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(0, session.SignedOut);

            var captchaFailure = await endpoint.InteractiveLogin(
                input with { CaptchaCode = "WRONG" },
                NewRequest(services), anti, captcha, auth, default);
            Assert.Equal(401, ((IStatusCodeHttpResult)captchaFailure).StatusCode);
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(0, session.SignedOut);
            Assert.Equal(0, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberFirstLoginChallenges]")
                .SingleAsync());

            var wrongContext = await endpoint.InteractiveLogin(
                input with { LoginContext = "system" },
                NewRequest(services), anti, captcha, auth, default);
            Assert.Equal(401, ((IStatusCodeHttpResult)wrongContext).StatusCode);
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(0, session.SignedOut);

            var request = NewRequest(services);
            var restricted = await endpoint.InteractiveLogin(
                input, request, anti, captcha, auth, default);
            Assert.Equal(200, ((IStatusCodeHttpResult)restricted).StatusCode);
            var value = ((IValueHttpResult)restricted).Value!;
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            var root = document.RootElement;
            Assert.Equal("password_change_required",
                root.GetProperty("status").GetString());
            var challenge = root.GetProperty("challenge").GetString()!;
            Assert.Equal(64, challenge.Length);
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(1, session.SignedOut);
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == pending.Member.Id)).IsActive);

            var completeRequest = new TenantFirstLoginWriter.CompleteRequest(
                "tenant-role-grants", challenge,
                "New-Strong-Permanent-Password-For-Idp-2026!");

            var foreignHost = NewRequest(services);
            foreignHost.Request.Host = new HostString("tenant-role-grants.shinera.localhost");
            var forbiddenHost = await endpoint.CompleteInteractiveFirstLogin(
                completeRequest, foreignHost, anti, default);
            Assert.Equal(400, ((IStatusCodeHttpResult)forbiddenHost).StatusCode);

            var csrfDenied = await endpoint.CompleteInteractiveFirstLogin(
                completeRequest, NewRequest(services),
                new PassThroughAntiforgery(reject: true), default);
            Assert.Equal(400, ((IStatusCodeHttpResult)csrfDenied).StatusCode);
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == pending.Member.Id)).IsActive);
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(1, session.SignedOut);

            var completed = await endpoint.CompleteInteractiveFirstLogin(
                new TenantFirstLoginWriter.CompleteRequest(
                    "tenant-role-grants", challenge,
                    "New-Strong-Permanent-Password-For-Idp-2026!"),
                NewRequest(services), anti, default);
            Assert.Equal(200, ((IStatusCodeHttpResult)completed).StatusCode);
            using var completeJson = JsonDocument.Parse(JsonSerializer.Serialize(
                ((IValueHttpResult)completed).Value));
            Assert.True(completeJson.RootElement.GetProperty("passwordChanged").GetBoolean());
            Assert.True(completeJson.RootElement.GetProperty("requiresRelogin").GetBoolean());
            Assert.Equal(0, session.SignedIn);
            Assert.Equal(2, session.SignedOut);

            anonymous.ChangeTracker.Clear();
            var active = await anonymous.Users.AsNoTracking()
                .SingleAsync(x => x.Id == pending.Member.Id);
            Assert.True(active.IsActive);
            Assert.Equal(PasswordVerificationResult.Success,
                hasher.VerifyHashedPassword(active, active.PasswordHash,
                    "New-Strong-Permanent-Password-For-Idp-2026!"));
            var replay = await endpoint.CompleteInteractiveFirstLogin(
                new TenantFirstLoginWriter.CompleteRequest(
                    "tenant-role-grants", challenge,
                    "A-Second-Permanent-Password-For-Idp-2026!"),
                NewRequest(services), anti, default);
            Assert.Equal(400, ((IStatusCodeHttpResult)replay).StatusCode);
            Assert.Equal(0, session.SignedIn);
        });
    }

    private sealed class FirstLoginTestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Foundation.Identity.SqlServerTests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class PassThroughAntiforgery(bool reject = false) : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) =>
            throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) =>
            throw new NotSupportedException();
        public Task<bool> IsRequestValidAsync(HttpContext context) =>
            Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext context) =>
            reject
                ? Task.FromException(new AntiforgeryValidationException("Denied test request"))
                : Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext context) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int SignedIn { get; private set; }
        public int SignedOut { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme,
            ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedIn++;
            return Task.CompletedTask;
        }
        public Task SignOutAsync(HttpContext context, string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOut++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Email_less_Tenant_staff_can_be_created_twice_and_initial_password_activates_without_email()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var first = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "no.email.one", null, "First", "NoEmail"));
            var second = await TenantPendingMemberWriter.CreateWithInitialPasswordAsync(
                actor, db, hasher, new TenantPendingMemberWriter.CreateRequest(
                    "no.email.two", "   ", "Second", "NoEmail"));
            Assert.Null(first.Email);
            Assert.Null(second.Member.Email);
            Assert.False(first.IsActive);
            Assert.False(second.Member.IsActive);
            Assert.Equal(27, second.TemporaryPassword.Length);

            db.ChangeTracker.Clear();
            var rows = await db.Users.AsNoTracking().Where(u =>
                u.Id == first.Id || u.Id == second.Member.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, u =>
            {
                Assert.Null(u.Email);
                Assert.Null(u.NormalizedEmail);
                Assert.False(u.EmailConfirmed);
                Assert.False(u.IsActive);
            });
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[Users] WHERE [Email] IS NULL AND [NormalizedEmail] IS NULL AND [TenantId] = {0}",
                actor.ActiveTenantId!.Value).SingleAsync());

            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            var normalAuthenticator = new UserCredentialAuthenticator(
                anonymous, hasher,
                new IdentityPrincipalFactory(anonymous, new DefaultSystemTenantScope()));
            Assert.Null(await normalAuthenticator.AuthenticateAsync(
                "no.email.two", second.TemporaryPassword,
                IdentityLoginContext.Tenant, "tenant-role-grants"));
            var resolved = await TenantFirstLoginWriter.ResolveForLoginAsync(
                anonymous, hasher, new TenantFirstLoginWriter.StartRequest(
                    "tenant-role-grants", "no.email.two", second.TemporaryPassword));
            Assert.Equal("password_change_required", resolved.Status);
            Assert.NotNull(resolved.Challenge);
            await TenantFirstLoginWriter.CompleteAsync(
                anonymous, hasher, new TenantFirstLoginWriter.CompleteRequest(
                    "tenant-role-grants", resolved.Challenge,
                    "New-Long-Employee-Password-2026!"));
            anonymous.ChangeTracker.Clear();
            var activated = await anonymous.Users.AsNoTracking()
                .SingleAsync(u => u.Id == second.Member.Id);
            Assert.True(activated.IsActive);
            Assert.Null(activated.Email);
            Assert.False(activated.EmailConfirmed);
            Assert.Equal(PasswordVerificationResult.Success,
                hasher.VerifyHashedPassword(activated, activated.PasswordHash,
                    "New-Long-Employee-Password-2026!"));
            Assert.False(await anonymous.UserRoles.IgnoreQueryFilters(["tenant"])
                .AnyAsync(r => r.UserId == activated.Id));

            // The exact *real* login pipeline must work without a fabricated
            // email or an invalid OpenIddict Claim(email, null) exception.
            var signedIn = await normalAuthenticator.AuthenticateAsync(
                "no.email.two", "New-Long-Employee-Password-2026!",
                IdentityLoginContext.Tenant, "tenant-role-grants");
            Assert.NotNull(signedIn);
            Assert.True(signedIn.Identity?.IsAuthenticated);
            Assert.Null(signedIn.FindFirst("email"));
            Assert.Equal(activated.Id.ToString(), signedIn.FindFirst("sub")?.Value);
            Assert.Equal("tenant-role-grants", signedIn.FindFirst("tenant")?.Value);
        });
    }

    [Fact]
    public async Task Email_invitations_reject_missing_recipient_without_creating_token_or_audit()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "no.email.invite", null, "No", "Email"));
            var fake = new FakeInvitationSender();
            await Assert.ThrowsAsync<TenantInvitationTargetException>(() =>
                TenantMemberInvitationWriter.InviteAsync(actor, db, fake, member.Id));
            Assert.Null(fake.Token);
            Assert.Null(fake.Recipient);
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations]")
                .SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberLifecycleAuditEntries] WHERE [Action] LIKE 'invite_%'")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Removing_email_after_invite_revokes_redemption_authority_without_500_or_activation()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            var member = await TenantPendingMemberWriter.CreateAsync(
                actor, db, hasher, new TenantPendingMemberWriter.CreateRequest(
                    "email.removed", "before.removal@example.test", "Mail", "Removed"));
            var sender = new FakeInvitationSender();
            await TenantMemberInvitationWriter.InviteAsync(actor, db, sender, member.Id);
            var altered = await setup.Users.SingleAsync(x => x.Id == member.Id);
            altered.Email = null;
            altered.NormalizedEmail = null;
            await setup.SaveChangesAsync();

            await using var anonymous = new IdentityDbContext(options, new TenantContext());
            await Assert.ThrowsAsync<TenantInvitationValidationException>(() =>
                TenantMemberInvitationWriter.ActivateAsync(anonymous, hasher,
                    new TenantMemberInvitationWriter.ActivateRequest(
                        "tenant-role-grants", sender.Token, "P@ssword-For-St4ff!")));
            Assert.False((await anonymous.Users.AsNoTracking()
                .SingleAsync(u => u.Id == member.Id)).IsActive);
            Assert.Equal(0, await anonymous.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantMemberInvitations] WHERE [UsedAtUtc] IS NOT NULL")
                .SingleAsync());
        });
    }

    [Fact]
    public async Task Actual_emails_stay_unique_per_Tenant_while_multiple_missing_emails_are_allowed()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "mail.one", "staff.real@example.test", "Actual", "Email"));
            await Assert.ThrowsAsync<TenantMemberCreateConflictException>(() =>
                TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                    new TenantPendingMemberWriter.CreateRequest(
                        "mail.two", "STAFF.REAL@example.test", "Duplicate", "Email")));
            await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "mail.empty.one", null, "No", "Mail"));
            await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "mail.empty.two", null, "No", "Mail"));

            var filter = await db.Database.SqlQueryRaw<string>(
                """
                SELECT [filter_definition] AS [Value]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID('dbo.Users')
                  AND [name] = 'IX_Users_TenantId_NormalizedEmail'
                """).SingleAsync();
            Assert.Contains("[NormalizedEmail] IS NOT NULL", filter);
            Assert.Contains("[TenantId] IS NOT NULL", filter);
            Assert.Equal(2, await db.Users.AsNoTracking().CountAsync(u =>
                u.TenantId == actor.ActiveTenantId && u.NormalizedEmail == null));
        });
    }

    [Fact]
    public async Task Member_create_rejects_malformed_nonempty_email_but_accepts_null()
    {
        await WithDisposableDatabaseAsync(async (setup, options) =>
        {
            var (owned, actor, _, _, _) = await SeedAsync(setup, options);
            await using var db = owned;
            var hasher = new PasswordHasher<User>();
            await Assert.ThrowsAsync<TenantMemberCreateValidationException>(() =>
                TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                    new TenantPendingMemberWriter.CreateRequest(
                        "bad.email", "invalid@@mail", "Bad", "Email")));
            var accepted = await TenantPendingMemberWriter.CreateAsync(actor, db, hasher,
                new TenantPendingMemberWriter.CreateRequest(
                    "valid.no.email", null, "Valid", "Employee"));
            Assert.Null(accepted.Email);
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
