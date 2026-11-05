using Application.SharedKernel.Exceptions;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Roles.Commands
{
    public sealed class UpdateRolePermissionsCommand : ICommand
    {
        [global::System.Text.Json.Serialization.JsonIgnore]
        public Guid StorageTenantId { get; set; }
        public Guid? RoleId { get; set; }

        public List<Guid> PermissionIds { get; set; } = new();
    }
    public sealed class UpdateRolePermissionsCommandHandler
    : ICommandHandler<UpdateRolePermissionsCommand>
    {
        private readonly IIdentityDbContext _context;

        public UpdateRolePermissionsCommandHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task Handle(
            UpdateRolePermissionsCommand request,
            CancellationToken cancellationToken)
        {
            if (request.StorageTenantId == Guid.Empty)
                throw new UnauthorizedAccessException("A verified storage scope is required.");
            var permissionIds = request.PermissionIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            var roleExists = await _context.Roles
                .AsNoTracking()
                .AnyAsync(role => role.Id == request.RoleId && role.TenantId == request.StorageTenantId, cancellationToken);

            if (!roleExists)
            {
                throw new ServiceExeption("Role not found.");
            }

            var validPermissionIds = await _context.Permissions
                .AsNoTracking()
                .Where(permission =>
                    permissionIds.Contains(permission.Id) &&
                    permission.IsActive)
                .Select(permission => permission.Id)
                .ToListAsync(cancellationToken);

            if (validPermissionIds.Count != permissionIds.Count)
            {
                throw new ServiceExeption("One or more permissions are invalid.");
            }

            var existingRolePermissions = await _context.RolePermissions
                .Where(rolePermission => rolePermission.RoleId == request.RoleId && rolePermission.TenantId == request.StorageTenantId)
                .ToListAsync(cancellationToken);

            _context.RolePermissions.RemoveRange(existingRolePermissions);

            var newRolePermissions = validPermissionIds.Select(permissionId =>
                new RolePermission(request.StorageTenantId, request.RoleId!.Value, permissionId));

            await _context.RolePermissions.AddRangeAsync(
                newRolePermissions,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

}
