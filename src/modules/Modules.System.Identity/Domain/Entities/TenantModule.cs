using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class TenantModule : AuditableEntity, IMustHaveTenant
    {
        public Guid TenantId { get; set; }
        public Guid ModuleId { get; set; }

        // این فیلدها اجازه می‌دهند تنظیمات پلن را برای یک مشتری خاص شخصی‌سازی کنیم
        public bool IsEnabled { get; set; }
        public DateTime? CustomExpiryDate { get; set; }
        public bool IsTrial { get; set; }

        public virtual Module? Module { get; set; }
        public virtual Tenant? Tenant { get;set;  }
        public TenantModule() : base(14) { }
        public TenantModule(ulong id) : base(id, 14) { }
        public TenantModule(Guid id) : base(id) { }

        public TenantModule( Module? module, Tenant? tenant, bool isTrial= false,bool isEnabled=true ) : base(14)
        {
            IsEnabled = isEnabled;
            TenantId = tenant.Id;
            ModuleId=module.Id; 
            CustomExpiryDate = DateTime.Now.AddYears(1);
            IsTrial = isTrial;
        }

        public TenantModule(Guid id, Guid moduleId, Guid tenantId) :base(id)
        {
            TenantId = tenantId;
            ModuleId = moduleId;
        }
    }
}
