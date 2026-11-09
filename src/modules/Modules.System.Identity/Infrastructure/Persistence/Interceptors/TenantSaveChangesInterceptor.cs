using Domain.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Fail-closed write guard for every IMustHaveTenant record.
/// Read-scope overrides NEVER authorize writes.
/// </summary>
public sealed class TenantSaveChangesInterceptor(
    ITenantContext tenant, TrustedTenantWriteScope trustedWrites) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        GuardWrites(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        GuardWrites(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void GuardWrites(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var original = (Guid)entry.Property(nameof(IMustHaveTenant.TenantId)).OriginalValue!;
                if (original != entry.Entity.TenantId)
                    throw new UnauthorizedAccessException("Tenant reassignment is forbidden.");
            }

            if (trustedWrites.IsBootstrap) continue;

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenant.ActiveTenantId
                    ?? throw new UnauthorizedAccessException("An active writable tenant is required.");

            var targetTenant = entry.Entity.TenantId;

            if (trustedWrites.AllowsPlatformTenant(targetTenant))
            {
                // Master-scoped authority alone must never allow direct grant
                // escalation. Explicit grant intents are bound to one target.
                // Assignment links are created or removed, never silently
                // rewritten to a different user, role, group or permission.
                if (entry.State == EntityState.Modified &&
                    entry.Entity is Modules.System.Identity.Domain.Entities.UserRole
                        or Modules.System.Identity.Domain.Entities.GroupRole
                        or Modules.System.Identity.Domain.Entities.UserPermission
                        or Modules.System.Identity.Domain.Entities.UserGroup
                        or Modules.System.Identity.Domain.Entities.RolePermission)
                    throw new UnauthorizedAccessException(
                        "Platform grant reassignment is forbidden; revoke and assign instead.");

                if (!trustedWrites.AllowsPlatformGrantMutation(entry.Entity))
                    throw new UnauthorizedAccessException(
                        "Platform grant mutation requires a verified operation and matching target.");
                continue;
            }

            // A System account cannot pick a TenantId in a request payload and
            // thereby gain permission to write to that tenant.
            if (tenant.IsSystemContext || tenant.UserId is null ||
                targetTenant == Guid.Empty ||
                !tenant.WritableTenantIds.Contains(targetTenant))
            {
                throw new UnauthorizedAccessException("Write denied outside the authorized tenant scope.");
            }

            // FR-12E: tenant write scope alone is NOT grant-management
            // authorization. A target-bound grant intent is required for
            // ordinary Tenant UserRole/RolePermission changes.
            if (entry.Entity is UserRole or RolePermission or UserGroup or GroupRole)
            {
                if (entry.State == EntityState.Modified)
                    throw new UnauthorizedAccessException(
                        "Tenant grant links are immutable; revoke and assign.");

                // Public signup is the only allowed un-audited Owner
                // bootstrap. Both the owner user and Owner role must be NEW
                // in this exact SaveChanges, with the server-provisioned new
                // Tenant. Existing Tenant accounts cannot forge this path.
                if (entry.State == EntityState.Added &&
                    entry.Entity is UserRole initial &&
                    IsNewSignupOwner(context, targetTenant, initial))
                    continue;

                if (!trustedWrites.AllowsTenantGrantMutation(entry.Entity))
                    throw new UnauthorizedAccessException(
                        "Tenant grant mutation requires a reviewed, target-bound operation.");
            }

            // GroupRole/UserGroup now require exact target-bound trusted intents
            // above, while direct UserPermission remains categorically denied.
            // Never let generic CRUD write privileged relationships.
            if (entry.Entity is UserPermission)
                throw new UnauthorizedAccessException(
                    "Tenant group/user permission grants require a dedicated audited API.");
        }
    }

    private bool IsNewSignupOwner(
        DbContext db, Guid tenantId, UserRole assignment)
    {
        if (tenant.UserId != assignment.UserId ||
            tenant.HomeTenantId != tenantId ||
            tenant.ActiveTenantId != tenantId ||
            !tenant.ReadableTenantIds.Contains(tenantId))
            return false;

        var userAdded = db.ChangeTracker.Entries<User>().Any(e =>
            e.State == EntityState.Added &&
            e.Entity.Id == assignment.UserId &&
            e.Entity.TenantId == tenantId &&
            e.Entity.UserType == UserType.Tenant);
        var ownerRoleAdded = db.ChangeTracker.Entries<Role>().Any(e =>
            e.State == EntityState.Added &&
            e.Entity.Id == assignment.RoleId &&
            e.Entity.TenantId == tenantId &&
            e.Entity.Scope == RoleScope.Tenant &&
            e.Entity.NormalizedName == "OWNER");
        var tenantAdded = db.ChangeTracker.Entries<Tenant>().Any(e =>
            e.State == EntityState.Added && e.Entity.Id == tenantId);

        return userAdded && ownerRoleAdded && tenantAdded;
    }
}
