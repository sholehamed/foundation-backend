using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Permission : AuditableEntity
    {
        public string Code { get; set; }
        public Guid? ResourceId { get; set; }
        public virtual Resource? Resource { get; set; }
        public string Name { get; set; } = default!;    // e.g. "invoice.read"

        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public Permission() : base(7) { }
        public Permission(ulong id) : base(id, 7) { }
        public Permission(Guid id) : base(id) { }

        public Permission(string code, Resource? resource, string name, string? description, bool isActive = true, List<Guid> apis = null, List<Guid> uis = null) : base(7)
        {
            Code = code.ToUpperInvariant();
            ResourceId = resource.Id;
            Resource = resource;
            Name = name;
            Description = description;
            IsActive = isActive;
            this.ApiResources = apis.Select(x => new PermissionApiResource(this, x)).ToList();
            this.UiResources = uis.Select(x => new PermissionUiResource(this, x)).ToList();
        }
        public Permission(Guid id, string code, Guid? resourceId, string name, string? description, bool isActive = true, List<Guid> apis = null, List<Guid> uis = null) : base(id)
        {
            Code = code.ToUpperInvariant();
            ResourceId = resourceId;
            Name = name;
            Description = description;
            IsActive = isActive;
            if (apis != null)
                this.ApiResources = apis.Select(x => new PermissionApiResource(this.Id, x)).ToList();
            if (uis != null)
                this.UiResources = uis.Select(x => new PermissionUiResource(this.Id, x)).ToList();
        }



        public ICollection<RolePermission> RolePermissions { get; set; } = [];
        public ICollection<UserPermission> UserPermissions { get; set; } = [];
        public ICollection<PermissionApiResource> ApiResources { get; set; } = [];
        public ICollection<Menu> Menus { get; set; } = [];
        public virtual ICollection<PermissionUiResource>? UiResources { get; set; } = [];
    }


}
