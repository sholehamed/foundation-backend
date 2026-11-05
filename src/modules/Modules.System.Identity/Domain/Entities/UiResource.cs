using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public enum UiResourceType
    {
        PAGE,
        ACTION,
        FIELD,
        WIDGET,
        
    }
    public class UiResource:AuditableEntity
    {
        public UiResource() : base(16) { }
        public UiResource(ulong id) : base(id, 16) { }
        public UiResource(Guid id) : base(id) { }

        public UiResource(Guid resourceId, string key, string title, string? description, UiResourceType type) : base(16)
        {
            ResourceId = resourceId;
            Key = key;
            Title = title;
            Description = description;
            Type = type;
            IsActive = true;
        }
        public UiResource(Guid id,Guid resourceId, string key, string title, string? description, UiResourceType type) : base(id)
        {
            ResourceId = resourceId;
            Key = key;
            Title = title;
            Description = description;
            Type = type;
            IsActive = true;
        }

        public Guid ResourceId { get; set; }
        public virtual Resource? Resource { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public UiResourceType Type { get; set; }
        public bool IsActive { get; set; }
        public virtual ICollection<PermissionUiResource>? PermissionUiResources { get; set; } = [];

    }
}