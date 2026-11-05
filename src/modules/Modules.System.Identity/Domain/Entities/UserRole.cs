using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class UserRole : AuditableEntity, IMustHaveTenant
    {
        public UserRole() : base(20) { }
        public UserRole(ulong id) : base(id, 20) { }
        public UserRole(Guid id) : base(id) { }
        public UserRole(Guid tenantId, Guid userId, Guid roleId) : base(20)
        {
            TenantId = tenantId;
            UserId = userId;
            RoleId = roleId;
        }
        public UserRole(Guid id,Guid tenantId, Guid userId, Guid roleId) : base(id)
        {
            TenantId = tenantId;
            UserId = userId;
            RoleId = roleId;
        }
        public Guid TenantId { get; set; }

        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public User User { get; set; } = default!;

        public Role Role { get; set; } = default!;
    }

}
