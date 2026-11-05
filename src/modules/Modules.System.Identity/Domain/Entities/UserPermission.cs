using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
   
    public class UserPermission : AuditableEntity, IMustHaveTenant
    {
        public UserPermission() : base(19) { }
        public UserPermission(ulong id) : base(id, 19) { }
        public UserPermission(Guid id) : base(id) { }
        public Guid TenantId { get; set; }

        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public Guid PermissionId { get; set; }
        public Permission Permission { get; set; } = default!;

        public bool IsGranted { get; set; } = true;
    }


}
