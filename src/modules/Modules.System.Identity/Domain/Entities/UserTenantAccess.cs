using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class UserTenantAccess:AuditableEntity
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }

        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public bool IncludeDescendants { get; set; }

        /// <summary>
        /// Optional deny rule for future extensibility.
        /// If true, this record acts as a deny entry.
        /// </summary>
        public bool IsDenied { get; set; }

        public string? Reason { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public virtual User? User { get; set; }
        public virtual Tenant? Tenant { get; set; } 
    }

}
