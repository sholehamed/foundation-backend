using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class RolePermission : AuditableEntity, IMustHaveTenant
    {
        public RolePermission() : base(12) { }
        public RolePermission(ulong id) : base(id, 12) { }
        public RolePermission(Guid id) : base(id) { }
        public RolePermission(Guid tenantId, Guid roleId, Guid permissionId) : base(12)
        {
            TenantId = tenantId;
            RoleId = roleId;
            PermissionId = permissionId;
        }

        public Guid TenantId { get; set; }

        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }
        public virtual Tenant? Tenant { get; set; }
        public virtual Role? Role { get; set; } = default!;

        public virtual Permission? Permission { get; set; } = default!;
    }

}
