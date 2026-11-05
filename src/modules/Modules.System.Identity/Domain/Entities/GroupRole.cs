using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class GroupRole : FullAuditableEntity, IMustHaveTenant, IHardDeleteAssignment
    {
        public Guid TenantId { get; set; }
        public Guid RoleId { get; set; }
        public Guid GroupId { get; set; }
        public virtual Role? Role { get; set; }
        public virtual Group? Group { get; set; }
        public GroupRole() : base(3) { }
        public GroupRole(ulong id) : base(id, 3) { }
        public GroupRole(Guid id) : base(id) { }

    }
}
