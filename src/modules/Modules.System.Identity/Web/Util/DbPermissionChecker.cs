using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util
{
    public class DbPermissionChecker(IIdentityDbContext dbContext) : IPermissionChecker
    {
        public async Task<bool> HasPermissionAsync(
            ClaimsPrincipal user,
            string apiResourceKey,
            CancellationToken cancellationToken = default)
        {
            var subject = user.FindFirstValue(OpenIddictConstants.Claims.Subject);
            var tenantIdValue = user.FindFirstValue("tenant_id");

            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantIdValue))
                return false;

            if (!Guid.TryParse(subject, out var userId))
                return false;

            if (!Guid.TryParse(tenantIdValue, out var tenantId))
                return false;

            var resourcePermissionIds = dbContext.PermissionApiResources
                .Where(x =>
                    x.ApiResource != null &&
                    x.ApiResource.Key == apiResourceKey &&
                    x.ApiResource.IsActive &&
                    x.Permission != null &&
                    x.Permission.IsActive)
                .Select(x => x.PermissionId)
                .Distinct();

            var deniedPermissionIds = dbContext.UserPermissions
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.UserId == userId &&
                    !x.IsGranted &&
                    x.Permission.IsActive)
                .Select(x => x.PermissionId);

            var grantedDirectPermissionIds = dbContext.UserPermissions
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.UserId == userId &&
                    x.IsGranted &&
                    x.Permission.IsActive)
                .Select(x => x.PermissionId);

            var grantedRolePermissionIds = dbContext.UserRoles
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.UserId == userId &&
                    x.Role.IsActive)
                .SelectMany(x => x.Role.RolePermissions
                    .Where(rp => rp.Permission.IsActive)
                    .Select(rp => rp.PermissionId));

            var grantedGroupPermissionIds = await dbContext.UserGroups
     .Where(x =>
         x.TenantId == tenantId &&
         x.UserId == userId &&
         x.Group.IsActive)
     .SelectMany(x => x.Group.GroupRoles
         .Where(gr => gr.Role.IsActive) // بررسی فعال بودن نقش‌های گروه (در صورت وجود فلگ IsActive روی Role)
         .SelectMany(gr => gr.Role.RolePermissions
             .Where(rp => rp.Permission.IsActive) // بررسی فعال بودن خود پرمیشن
             .Select(rp => rp.PermissionId)))
     .Distinct()
     .ToListAsync(cancellationToken);


            var effectiveGrantedPermissionIds = grantedDirectPermissionIds
                .Union(grantedRolePermissionIds)
                .Union(grantedGroupPermissionIds)
                .Except(deniedPermissionIds)
                .Distinct();

            return await resourcePermissionIds
                .Intersect(effectiveGrantedPermissionIds)
                .AnyAsync(cancellationToken);
        }
    }
}