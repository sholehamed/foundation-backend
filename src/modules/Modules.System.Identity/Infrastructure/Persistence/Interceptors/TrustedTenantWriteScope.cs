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
