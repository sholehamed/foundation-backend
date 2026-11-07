using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// A shared live-DB Resource.Action evaluator. The calling authorization handler
/// must bind userId and scopeTenantId to a validated System/Tenant principal.
/// This method does NOT accept role IDs or tenant IDs from a request body.
/// System users have no home tenant: the Master Tenant holds their role grants.
/// </summary>
public sealed class EffectivePermissionEvaluator(IIdentityDbContext db)
{
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid scopeTenantId,
        UserType accountType,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || scopeTenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(permissionCode) ||
            accountType is not (UserType.System or UserType.Tenant))
            return false;

        var now = DateTime.UtcNow;
        var accountActive = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId &&
            user.IsActive &&
            (!user.IsLockedOut || user.LockoutEndUtc <= now) &&
            (accountType == UserType.System
                ? user.UserType == UserType.System && user.TenantId == null
                : user.UserType == UserType.Tenant && user.TenantId == scopeTenantId),
            cancellationToken);
        if (!accountActive)
            return false;

        if (!await db.Tenants.AsNoTracking().AnyAsync(t =>
                t.Id == scopeTenantId && t.IsActive, cancellationToken))
            return false;

        var directAssignments = db.UserPermissions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.TenantId == scopeTenantId &&
                        x.Permission.IsActive &&
                        x.Permission.Code == permissionCode);

        // Direct deny always defeats all role/group inherited grants.
        if (await directAssignments.AnyAsync(x => !x.IsGranted, cancellationToken))
            return false;
        if (await directAssignments.AnyAsync(x => x.IsGranted, cancellationToken))
            return true;

        if (await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking().AnyAsync(x =>
                x.UserId == userId &&
                x.TenantId == scopeTenantId &&
                x.Role.TenantId == scopeTenantId &&
                x.Role.IsActive &&
                x.Role.RolePermissions.Any(rp =>
                    rp.TenantId == scopeTenantId &&
                    rp.Permission.IsActive &&
                    rp.Permission.Code == permissionCode), cancellationToken))
            return true;

        return await db.UserGroups.IgnoreQueryFilters(["tenant"]).AsNoTracking().AnyAsync(x =>
            x.UserId == userId &&
            x.TenantId == scopeTenantId &&
            x.Group.TenantId == scopeTenantId &&
            x.Group.IsActive &&
            x.Group.GroupRoles.Any(gr =>
                gr.TenantId == scopeTenantId &&
                gr.Role.TenantId == scopeTenantId &&
                gr.Role.IsActive &&
                gr.Role.RolePermissions.Any(rp =>
                    rp.TenantId == scopeTenantId &&
                    rp.Permission.IsActive &&
                    rp.Permission.Code == permissionCode)), cancellationToken);
    }
}
