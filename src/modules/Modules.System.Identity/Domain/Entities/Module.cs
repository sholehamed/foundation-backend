using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Module:AuditableEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public virtual ICollection<Resource>? Resources { get; set; }
        public Module() : base(6) { }
        public Module(ulong id) : base(id, 6) { }
        public Module(Guid id) : base(id) { }

        public Module(string code, string title, string? description, int sortOrder, bool isActive=true) : base(6)
        {
            Code = code;
            Title = title;
            Description = description;
            SortOrder = sortOrder;
            IsActive = isActive;
        }
        public Module(Guid id,string code, string title, string? description, int sortOrder, bool isActive = true) : base(id)
        {
            Code = code;
            Title = title;
            Description = description;
            SortOrder = sortOrder;
            IsActive = isActive;
        }
    }
}
