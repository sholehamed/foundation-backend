using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class Menu:AuditableEntity
    {
        public Guid CategoryId { get; set; }
        public virtual MenuCategory? Category { get; set; }
        public Guid? ParentId { get; set; }
        public virtual Menu? Parent { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public  string Title { get; set; }
        public  string? Icon { get; set; }
        public short Order { get; set; }
        public string? Route { get; set; }
        public string? ExternalUrl { get; set; }
        public Guid? PermissionId { get; set; }
        public virtual Permission? Permission { get; set; }
        public virtual ICollection<Menu>? Childs { get; set; }
        public Menu() : base(4) { }
        public Menu(ulong id) : base(id, 4) { }
        public Menu(Guid id) : base(id) { }
        public Menu(string title,string icon,short order,string route,Guid permission) : base(4)
        {
            Title = title;
            Icon = icon;
            Order = order;
            Route = route;
            PermissionId=permission;
            IsActive= true;
            IsHidden = false;
        }
    }
}
