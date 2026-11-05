using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class PermissionApiResource:AuditableEntity
    {
        public PermissionApiResource() : base(8) { }
        public PermissionApiResource(ulong id) : base(id, 8) { }
        public PermissionApiResource(Guid id) : base(id) { }

        public PermissionApiResource(Guid permissionId, Guid apiResourceId) : base(8)
        {
            PermissionId = permissionId;
            ApiResourceId = apiResourceId;
        }

        public PermissionApiResource( Permission? permission, Guid apiResourceId) : base(8)
        {
            Permission = permission;
            ApiResourceId = apiResourceId;
        }

        public Guid PermissionId { get; set; }
        public virtual Permission? Permission { get; set; }
        public Guid ApiResourceId { get; set; }
        public virtual ApiResource? ApiResource { get; set; }
    }
}