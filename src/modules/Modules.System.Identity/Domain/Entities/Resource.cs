using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Resource:AuditableEntity
    {
         public Guid ModuleId { get; set; }
        public virtual Module? Module { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public Resource(Guid moduleId,  string code, string title, string? description, int sortOrder, bool isActive=true) : base(10)
        {
            ModuleId = moduleId;
            Code = code;
            Title = title;
            Description = description;
            SortOrder = sortOrder;
            IsActive = isActive;
        }
        public Resource(Guid id,Guid moduleId, string code, string title, string? description, int sortOrder, bool isActive = true) : base(id)
        {
            ModuleId = moduleId;
            Code = code;
            Title = title;
            Description = description;
            SortOrder = sortOrder;
            IsActive = isActive;
        }
        public Resource() : base(10) { }
        public Resource(ulong id) : base(id, 10) { }
        public Resource(Guid id) : base(id) { }

        public ICollection<Permission>? Permissions { get; set; }

        public virtual ICollection<UiResource>? UiResources { get; set; }
        public virtual ICollection<ApiResource>? ApiResources { get; set; }
    }
}