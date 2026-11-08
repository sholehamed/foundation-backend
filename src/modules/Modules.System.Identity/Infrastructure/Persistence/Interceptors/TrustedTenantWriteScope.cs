using Microsoft.Extensions.Logging;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Infrastructure-owned write overrides. Query-filter bypass is intentionally NOT
/// a write bypass. Only trusted infrastructure code can open these scopes.
/// </summary>
internal enum PlatformGrantMutationKind { RolePermissions, GroupMembers, GroupMemberRemoval, UserRoles, GroupRoles, UserPermissions }

public sealed class TrustedTenantWriteScope(ILogger<TrustedTenantWriteScope> logger)
{
    private int _bootstrapDepth;
    private Guid? _platformTenant;
    private Guid? _platformActor;

    private PlatformGrantIntent? _grantIntent;
    private TenantGrantIntent? _tenantGrantIntent;

    private sealed record TenantGrantIntent(
        Guid TenantId, Guid ActorId, string Kind, Guid TargetId,
        HashSet<Guid> RelatedIds);

    /// <summary>
    /// This is a narrow storage-layer intent, NOT a substitute for the live
    /// Owner, target, permission, version and SQL audit checks in the writer.
    /// It prevents ordinary TenantDbContext.SaveChanges calls from silently
    /// creating/rewriting UserRole and RolePermission links.
    /// </summary>
    public IDisposable BeginTenantGrantMutation(
        ITenantContext subject, Guid targetId, string kind,
        IReadOnlyCollection<Guid> relatedIds)
    {
        if (subject.IsSystemContext ||
            subject.UserId is not Guid actor || actor == Guid.Empty ||
            subject.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            subject.HomeTenantId != tenantId ||
            !subject.WritableTenantIds.Contains(tenantId) ||
            !subject.ReadableTenantIds.Contains(tenantId) ||
            targetId == Guid.Empty || targetId == actor ||
            kind is not ("UserRole" or "RolePermission") ||
            relatedIds.Count == 0 || relatedIds.Any(id => id == Guid.Empty) ||
            relatedIds.Distinct().Count() != relatedIds.Count ||
            _tenantGrantIntent is not null || _platformActor is not null ||
            IsBootstrap)
            throw new UnauthorizedAccessException(
                "An explicit, non-nested, writable Tenant grant scope is required.");

        _tenantGrantIntent = new TenantGrantIntent(
            tenantId, actor, kind, targetId, relatedIds.ToHashSet());
        return new GuardedScope(() => _tenantGrantIntent = null);
    }

    internal bool AllowsTenantGrantMutation(object entity) =>
        _tenantGrantIntent is { } intent && entity switch
        {
            UserRole ur => intent.Kind == "UserRole" &&
                ur.TenantId == intent.TenantId &&
                ur.UserId == intent.TargetId &&
                intent.RelatedIds.Contains(ur.RoleId),
            RolePermission rp => intent.Kind == "RolePermission" &&
                rp.TenantId == intent.TenantId &&
                rp.RoleId == intent.TargetId &&
                intent.RelatedIds.Contains(rp.PermissionId),
            _ => false
        };

    private sealed record PlatformGrantIntent(PlatformGrantMutationKind Kind, Guid TargetId, Guid? RelatedId);

    // The master write gateway does not authorize arbitrary privileged grant
    // changes. Only the named, independently-validated operation can open this
    // more restrictive inner scope, and only for its designated target.
    internal IDisposable BeginPlatformGrantMutation(PlatformGrantMutationKind kind, Guid targetId, Guid? relatedId = null)
    {
        if (_platformActor is null ||
            _platformTenant != DefaultSeedData.MasterTenant.Id ||
            targetId == Guid.Empty || _grantIntent is not null ||
            ((kind is PlatformGrantMutationKind.UserRoles or PlatformGrantMutationKind.GroupRoles or PlatformGrantMutationKind.UserPermissions or PlatformGrantMutationKind.GroupMemberRemoval) &&
             (relatedId is null || relatedId == Guid.Empty)))
            throw new UnauthorizedAccessException("A verified, non-nested platform grant operation is required.");

        _grantIntent = new PlatformGrantIntent(kind, targetId, relatedId);
        logger.LogWarning("Scoped platform grant write opened for {Kind} {TargetId}", kind, targetId);
        return new GuardedScope(() =>
        {
            _grantIntent = null;
            logger.LogWarning("Scoped platform grant write closed for {Kind} {TargetId}", kind, targetId);
        });
    }

    internal bool AllowsPlatformGrantMutation(object entity) => entity switch
    {
        // Always protected from generic master writes. These assignments
        // need separate audited, target-validated grant APIs.
        UserRole ur => _grantIntent is
            { Kind: PlatformGrantMutationKind.UserRoles } intent &&
            ur.UserId == intent.TargetId &&
            ur.RoleId == intent.RelatedId,
        GroupRole gr => _grantIntent is
            { Kind: PlatformGrantMutationKind.GroupRoles } intent &&
            gr.GroupId == intent.TargetId &&
            gr.RoleId == intent.RelatedId,
        UserPermission up => _grantIntent is
            { Kind: PlatformGrantMutationKind.UserPermissions } intent &&
            up.UserId == intent.TargetId &&
            up.PermissionId == intent.RelatedId,
        RolePermission rp => _grantIntent is
            { Kind: PlatformGrantMutationKind.RolePermissions } intent &&
            rp.RoleId == intent.TargetId,
        UserGroup member => (_grantIntent is
            { Kind: PlatformGrantMutationKind.GroupMembers } add &&
            member.GroupId == add.TargetId) ||
            (_grantIntent is
            { Kind: PlatformGrantMutationKind.GroupMemberRemoval } remove &&
             member.GroupId == remove.TargetId &&
             member.UserId == remove.RelatedId),
        _ => true
    };

    internal bool IsBootstrap => _bootstrapDepth > 0;
    internal bool AllowsPlatformTenant(Guid tenantId) =>
        _platformTenant == tenantId && _platformActor.HasValue;

    /// <summary>For startup migrations/seeding, never for a request handler.</summary>
    internal IDisposable BeginBootstrapWrites()
    {
        _bootstrapDepth++;
        logger.LogWarning("Trusted bootstrap write scope entered");
        return new GuardedScope(() =>
        {
            _bootstrapDepth--;
            logger.LogWarning("Trusted bootstrap write scope exited");
        });
    }

    /// <summary>
    /// Reserved for an explicitly authorized/audited platform operation.
    /// This is NOT opened automatically merely because a JWT says superadmin.
    /// </summary>
    internal IDisposable BeginPlatformMasterWrite(ITenantContext subject, Guid actorUserId, Guid storageTenantId)
    {
        if (!subject.IsSystemContext || !subject.IsSuperAdmin ||
            subject.UserId != actorUserId || actorUserId == Guid.Empty ||
            storageTenantId != DefaultSeedData.MasterTenant.Id ||
            _platformActor.HasValue)
        {
            throw new UnauthorizedAccessException("Platform write scope requires a verified system administrator and the master storage scope.");
        }

        _platformTenant = storageTenantId;
        _platformActor = actorUserId;
        logger.LogWarning("Trusted platform write scope entered for actor {ActorId}", actorUserId);
        return new GuardedScope(() =>
        {
            logger.LogWarning("Trusted platform write scope exited for actor {ActorId}", actorUserId);
            _grantIntent = null;
            _platformActor = null;
            _platformTenant = null;
        });
    }

    private sealed class GuardedScope(Action dispose) : IDisposable
    {
        private bool _closed;
        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            dispose();
        }
    }
}
