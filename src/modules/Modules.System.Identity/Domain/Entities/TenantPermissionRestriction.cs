using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class TenantPermissionRestriction : AuditableEntity, IMustHaveTenant
    {
        public TenantPermissionRestriction() : base(15) { }
        public TenantPermissionRestriction(ulong id) : base(id, 15) { }
        public TenantPermissionRestriction(Guid id) : base(id) { }
        public Guid TenantId { get; set; }
        public Guid PermissionId { get; set; }
        public Permission Permission { get; set; } = default!;
    }
}
