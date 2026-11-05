using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class UserGroup : AuditableEntity, IMustHaveTenant
    {
        public UserGroup() : base(18) { }
        public UserGroup(ulong id) : base(id, 18) { }
        public UserGroup(Guid id) : base(id) { }
        public Guid TenantId { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public Guid GroupId { get; set; }
        public Group Group { get; set; } = default!;
    }


}
