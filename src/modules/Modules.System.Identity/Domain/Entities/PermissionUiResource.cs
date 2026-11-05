using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class PermissionUiResource:AuditableEntity
    {
        public PermissionUiResource() : base(9) { }
        public PermissionUiResource(ulong id) : base(id, 9) { }
        public PermissionUiResource(Guid id) : base(id) { }

        public PermissionUiResource(Permission? permission, Guid uiResourceId) : base(9)
        {
            Permission = permission;
            UiResourceId = uiResourceId;
        }
        public PermissionUiResource(Guid permissionId, Guid apiResourceId) : base(9)
        {
            PermissionId = permissionId;
            UiResourceId = apiResourceId;
        }
        public Guid PermissionId { get; set; }
        public virtual Permission? Permission { get; set; }
        public Guid UiResourceId { get; set; }
        public virtual UiResource? UiResource { get; set; }
    }
}