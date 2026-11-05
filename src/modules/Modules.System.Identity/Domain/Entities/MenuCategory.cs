using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class MenuCategory:AuditableEntity
    {
        public  string Title { get; set; }
        public short Order { get; set; }
        public bool IsActive { get; set; }
        public virtual ICollection<Menu>? Menus { get; set; }
        public MenuCategory() : base(5) { }
        public MenuCategory(ulong id) : base(id, 5) { }
        public MenuCategory(Guid id) : base(id) { }
        public MenuCategory(string title,short order,List<Menu> menus,bool isActive=true) : base(5)
        {
            Title=title;
            Order = order;
            Menus = menus;
            IsActive = isActive;
        }
    }
}
