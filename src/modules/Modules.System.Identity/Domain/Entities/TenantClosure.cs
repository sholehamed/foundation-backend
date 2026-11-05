using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class TenantClosure:Entity
    {
        public Guid AncestorTenantId { get; set; }
        public Guid DescendantTenantId { get; set; }

        /// <summary>
        /// 0 = self
        /// 1 = direct child
        /// n = deeper descendant
        /// </summary>
        public int Depth { get; set; }
        public virtual Tenant? AncestorTenant { get; set; }
        public virtual  Tenant? DescendantTenant { get; set; }
    }
}
