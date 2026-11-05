using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public enum RoleScope : byte
    {
        System = 1,
        Tenant = 2
    }
    public class Role : AuditableEntity
    {
        public Role() : base(11) { }
        public Role(ulong id) : base(id, 11) { }
        public Role(Guid id) : base(id) { }
        public Role(Guid tenantId, string name,  string? description) : base(11)
        {
            TenantId = tenantId;
            Name = name;
            NormalizedName = name.Trim().ToUpperInvariant();
            Description = description;
        }
        public Role(Guid id,Guid tenantId, string name, string? description) : base(id)
        {
            TenantId = tenantId;
            Name = name;
            NormalizedName = name.Trim().ToUpperInvariant();
            Description = description;
        }
        public RoleScope Scope { get; set; }
        public Guid? TenantId { get; set; }
        public string Name { get; set; } = default!;
        public string NormalizedName { get; set; } = default!;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<UserRole> UserRoles { get; set; } = [];
        public ICollection<RolePermission> RolePermissions { get; set; } = [];
        public ICollection<GroupRole> GroupRoles { get; set; } = [];
    }

}
