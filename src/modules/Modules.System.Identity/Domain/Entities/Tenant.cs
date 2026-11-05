using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Tenant : AuditableEntity
    {
        public Guid? ParentId { get; set; }
        public virtual Tenant? Parent { get; set; }
        public string Name { get; set; } = default!;
        public string Slug { get; set; } = default!; // unique, e.g. "tenant-a"
        public string? Domain { get; set; }
        public Guid? Favicon { get; set; }
        public Guid? Logo { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<User> Users { get; set; } = [];
        public virtual ICollection<TenantModule> TenantModules { get; set; }
        public Tenant() : base(13) { }
        public Tenant(ulong id) : base(id, 13) { }
        public Tenant(Guid id) : base(id) { }
       
        public Tenant(Guid parentId,string name,string slug,string domain,bool isActive=true) : base(13)
        {
            ParentId = parentId;
            Name=name;
            Slug = slug;
            Domain = domain;
            IsActive = isActive;
        }

        public Tenant(Tenant? parent, string name, string slug, string domain, bool isActive = true) : base(13)
        {
            Parent = parent;
            Name = name;
            Slug = slug;
            Domain = domain;
            IsActive = isActive;
        }
        public Tenant(Guid id,Guid? parent, string name, string slug, string domain, bool isActive = true) : base(id)
        {
            ParentId = parent;
            Name = name;
            Slug = slug;
            Domain = domain;
            IsActive = isActive;
        }
    }



}
