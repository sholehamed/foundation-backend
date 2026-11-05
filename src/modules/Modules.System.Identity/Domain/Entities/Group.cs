using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Group : AuditableEntity, IMustHaveTenant
    {
        public string Name { get; set; } = default!;
        public Guid TenantId { get; set; }
        public string NormalizedName { get; set; } = default!;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<UserGroup> UserGroups { get; set; } = [];
        public ICollection<GroupRole> GroupRoles { get; set; } = [];
        public Group() : base(2) { }
        public Group(ulong id) : base(id, 2) { }
        public Group(Guid id) : base(id) { }
    }


}
