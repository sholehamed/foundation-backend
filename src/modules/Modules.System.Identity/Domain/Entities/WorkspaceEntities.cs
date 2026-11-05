using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities;

/// <summary>Business onboarding details; the selected plan is a request, NOT an active subscription.</summary>
public sealed class BusinessProfile : AuditableEntity, IMustHaveTenant
{
    public BusinessProfile() : base(31) { }
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = "";
    public string ActivityType { get; set; } = "";
    public string? Phone { get; set; }
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? Instagram { get; set; }
    public string Culture { get; set; } = "fa-IR";
    public string Timezone { get; set; } = "Asia/Tehran";
    public string Currency { get; set; } = "IRR";
    public string RequestedPlanKey { get; set; } = "";
    public string RequestedBillingCycle { get; set; } = "";
    public string? PromoCode { get; set; }
    public bool AcceptedTerms { get; set; }
    public bool AcceptedPrivacy { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public Tenant Tenant { get; set; } = default!;
}

/// <summary>Each workspace receives an actual Main Branch, including solo workspaces.</summary>
public sealed class Branch : AuditableEntity, IMustHaveTenant
{
    public Branch() : base(32) { }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public bool IsMain { get; set; }
    public Tenant Tenant { get; set; } = default!;
}
