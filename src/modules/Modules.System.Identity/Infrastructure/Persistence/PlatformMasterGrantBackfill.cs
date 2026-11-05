using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Offline-only building block for an explicitly reviewed fixed-Master
/// RolePermission batch. NOT registered in DI, Startup, endpoints or seeding.
/// The operator supplies an exact before-image and fixed new assignment IDs.
/// No generic "grant all" or automatic UserRole bootstrap is supported.
/// Approval signatures, operator authentication and production release
/// authority are OUTSIDE this primitive and must be implemented separately.
/// </summary>
public static class PlatformMasterGrantBackfill
{
    public sealed record Grant(Guid AssignmentId, Guid PermissionId);
    public sealed record Manifest(
        Guid MasterTenantId,
        Guid RoleId,
        Guid ActorUserId,
        Guid ReviewerUserId,
        string DatabaseName,
        IReadOnlyList<Grant> ExpectedBefore,
        IReadOnlyList<Grant> Additions);

    public static Task ApplyAsync(
        IdentityDbContext db, Manifest manifest, CancellationToken ct = default)
        => ExecuteAsync(db, manifest, rollback: false, ct);

    /// <summary>
    /// Reverses ONLY the exact assignment IDs from a prior approved manifest,
    /// provided the entire expected after-image is unchanged. No bulk revoke.
    /// Caller must verify the original approved manifest and external receipt.
    /// </summary>
    public static Task RollbackAsync(
        IdentityDbContext db, Manifest manifest, CancellationToken ct = default)
        => ExecuteAsync(db, manifest, rollback: true, ct);

    private static async Task ExecuteAsync(
        IdentityDbContext db, Manifest manifest, bool rollback, CancellationToken ct)
    {
        ValidateManifest(manifest);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var master = DefaultSeedData.MasterTenant.Id;

            // The app lock coordinates approved batches. Serializable range
            // locks on RolePermission also detect concurrent database changes.
            var lockName = $"Shinera.PlatformMaster.RolePermission.{manifest.RoleId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock
                    @Resource = {lockName},
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                IF @lockResult < 0
                    THROW 51050, 'Master grant batch lock unavailable.', 1;
                """, ct);

            // Explicit DB/actor/role validation within the SAME transaction.
            var databaseName = db.Database.GetDbConnection().Database;
            if (!string.Equals(manifest.DatabaseName, databaseName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Reviewed database identity mismatch.");

            if (!await db.Tenants.AsNoTracking().AnyAsync(t =>
                    t.Id == master && t.IsActive, ct))
                throw new UnauthorizedAccessException("Master storage tenant inactive.");

            if (!await db.Roles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                    .AnyAsync(r => r.Id == manifest.RoleId &&
                                   r.TenantId == master &&
                                   r.NormalizedName == "SUPERADMIN" && r.IsActive, ct))
                throw new UnauthorizedAccessException("Reviewed Master role is missing or inactive.");

            foreach (var id in new[] { manifest.ActorUserId, manifest.ReviewerUserId })
            {
                if (!await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                    .AnyAsync(ur => ur.TenantId == master &&
                        ur.RoleId == manifest.RoleId && ur.UserId == id &&
                        ur.User.UserType == UserType.System &&
                        ur.User.TenantId == null && ur.User.IsActive &&
                        (!ur.User.IsLockedOut || ur.User.LockoutEndUtc <= DateTime.UtcNow), ct))
                    throw new UnauthorizedAccessException(
                        "Both reviewed operators must be distinct, live System administrators.");
            }

            var ids = manifest.Additions.Select(x => x.PermissionId).ToArray();
            var validCodes = PlatformPermissionCatalog.Codes;
            var authorized = await db.Permissions.AsNoTracking()
                .Where(p => ids.Contains(p.Id) && p.IsActive &&
                            validCodes.Contains(p.Code))
                .Select(p => p.Id).ToListAsync(ct);
            if (authorized.Count != ids.Length)
                throw new UnauthorizedAccessException(
                    "Proposed grant includes missing, inactive or non-catalog Permission.");

            var actual = await db.RolePermissions.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().Where(x => x.TenantId == master &&
                                           x.RoleId == manifest.RoleId)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.PermissionId, x.CreatedBy })
                .ToListAsync(ct);

            var expected = rollback
                ? manifest.ExpectedBefore.Concat(manifest.Additions).ToArray()
                : manifest.ExpectedBefore.ToArray();

            // Require exact IDs AND target PermissionIDs. Backfill fails if an
            // unexpected assignment exists or a previously reviewed row moved.
            if (actual.Count != expected.Length ||
                !actual.Select(x => (x.Id, x.PermissionId)).ToHashSet()
                    .SetEquals(expected.Select(x => (x.AssignmentId, x.PermissionId))))
                throw new InvalidOperationException(
                    "Master RolePermission snapshot drift detected; batch aborted.");

            // Refuse rollback of an assignment created or replaced by someone
            // else. The manifest and external approval receipt remain necessary.
            if (rollback && actual.Any(row => manifest.Additions.Any(a =>
                    a.AssignmentId == row.Id &&
                    (a.PermissionId != row.PermissionId ||
                     row.CreatedBy != manifest.ActorUserId))))
                throw new InvalidOperationException(
                    "Grant provenance changed; rollback aborted.");

            foreach (var grant in manifest.Additions)
            {
                if (rollback)
                {
                    // A matching row and CreatedBy alone do not prove that a
                    // backfill was ever committed. Require its append-only
                    // grant audit evidence before attempting a compensating
                    // revoke. This is still NOT a cryptographic batch receipt:
                    // the legacy ledger does not store RolePermission.Id.
                    var priorAudit = await db.Database.SqlQuery<long>($"""
                        SELECT COUNT_BIG(*) AS [Value]
                        FROM [dbo].[PlatformGrantAuditEntries]
                        WHERE [TenantId] = {master}
                          AND [ActorUserId] = {manifest.ActorUserId}
                          AND [Kind] = {"RolePermission"}
                          AND [TargetId] = {manifest.RoleId}
                          AND [RelatedId] = {grant.PermissionId}
                          AND [Action] = {"grant"}
                          AND [PreviousGrant] = {false}
                          AND [CurrentGrant] = {true}
                        """).SingleAsync(ct);
                    if (priorAudit < 1)
                        throw new InvalidOperationException(
                            "Matching committed grant audit is missing; rollback denied.");

                    var removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM [dbo].[RolePermissions]
                        WHERE [Id] = {grant.AssignmentId}
                          AND [TenantId] = {master}
                          AND [RoleId] = {manifest.RoleId}
                          AND [PermissionId] = {grant.PermissionId}
                          AND [CreatedBy] = {manifest.ActorUserId}
                        """, ct);
                    if (removed != 1)
                        throw new InvalidOperationException(
                            "Expected backfill grant was not removed exactly once.");
                }
                else
                {
                    var createdAt = DateTime.UtcNow;
                    var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO [dbo].[RolePermissions]
                            ([Id], [TenantId], [RoleId], [PermissionId],
                             [CreatedBy], [CreatedAt])
                        VALUES
                            ({grant.AssignmentId}, {master}, {manifest.RoleId},
                             {grant.PermissionId}, {manifest.ActorUserId}, {createdAt})
                        """, ct);
                    if (inserted != 1)
                        throw new InvalidOperationException(
                            "Reviewed backfill grant was not inserted exactly once.");
                }

                // Same EF transaction: audit insert fault => ALL writes revert.
                await PlatformGrantAuditWriter.RecordAsync(db,
                    manifest.ActorUserId, "RolePermission",
                    manifest.RoleId, grant.PermissionId,
                    rollback ? "revoke" : "grant",
                    rollback, !rollback, ct);
            }

            await tx.CommitAsync(ct);
        });
    }

    private static void ValidateManifest(Manifest m)
    {
        var master = DefaultSeedData.MasterTenant.Id;
        if (m.MasterTenantId != master || m.RoleId == Guid.Empty ||
            m.ActorUserId == Guid.Empty || m.ReviewerUserId == Guid.Empty ||
            m.ActorUserId == m.ReviewerUserId ||
            string.IsNullOrWhiteSpace(m.DatabaseName) ||
            m.ExpectedBefore is null || m.Additions is null ||
            m.Additions.Count is < 1 or > 9 ||
            m.ExpectedBefore.Any(g => g.AssignmentId == Guid.Empty ||
                                     g.PermissionId == Guid.Empty) ||
            m.Additions.Any(g => g.AssignmentId == Guid.Empty ||
                                g.PermissionId == Guid.Empty) ||
            m.ExpectedBefore.Concat(m.Additions)
                .Select(x => x.AssignmentId).Distinct().Count() !=
                m.ExpectedBefore.Count + m.Additions.Count ||
            m.ExpectedBefore.Concat(m.Additions)
                .Select(x => x.PermissionId).Distinct().Count() !=
                m.ExpectedBefore.Count + m.Additions.Count)
            throw new ArgumentException("Invalid or ambiguous reviewed Master grant manifest.");
    }
}
