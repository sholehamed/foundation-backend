using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities;

/// <summary>
/// FR-17B-1 globally defined commercial Plan identity.
/// A parent is for catalog organization; paid capability inheritance is pinned
/// to an immutable ParentPlanVersionId on each published revision.
/// </summary>
public sealed class CatalogPlan : AuditableEntity
{
    public CatalogPlan() : base(41) { }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentPlanId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Published versions are immutable by the future command layer; the database
/// holds an explicit parent *version*, never a live parent Plan pointer.
/// This record itself never licenses a Tenant or grants a Permission.
/// </summary>
public sealed class CatalogPlanVersion : AuditableEntity
{
    public CatalogPlanVersion() : base(42) { }
    public Guid PlanId { get; set; }
    public Guid? ParentPlanVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? PublishedAtUtc { get; set; }
    public string? ContentFingerprint { get; set; }
    public string DisplayTitle { get; set; } = "";
    public string? DisplaySubtitle { get; set; }
    public string? BadgeText { get; set; }
    public bool IsRecommended { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Explicit extra module on one PlanVersion. Effective modules are the
/// distinct union with pinned parent revisions, subject to commercial checks.
/// </summary>
public sealed class CatalogPlanVersionModule : AuditableEntity
{
    public CatalogPlanVersionModule() : base(43) { }
    public Guid PlanVersionId { get; set; }
    public Guid ModuleId { get; set; }
}

/// <summary>
/// Catalog binding for a vetted, code-owned capability definition. Plan admin
/// can configure allowance values, not arbitrary executable C#/SQL/HTML.
/// </summary>
public sealed class CatalogCapabilityDefinition : AuditableEntity
{
    public CatalogCapabilityDefinition() : base(44) { }
    public string Code { get; set; } = "";
    public Guid ModuleId { get; set; }
    public Guid? ResourceId { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ValueKind { get; set; } = "";
    public string MergePolicy { get; set; } = "";
    public string? Unit { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Exactly one typed value is populated. Unlimited is an explicit state:
/// 0 means zero allowance, null means no numeric value, never 'unlimited'.
/// </summary>
public sealed class CatalogPlanVersionCapabilityValue : AuditableEntity
{
    public CatalogPlanVersionCapabilityValue() : base(45) { }
    public Guid PlanVersionId { get; set; }
    public Guid CapabilityDefinitionId { get; set; }
    public bool? BooleanValue { get; set; }
    public long? IntegerValue { get; set; }
    public string? ModeSetJson { get; set; }
    public bool IsUnlimited { get; set; }
}

/// <summary>
/// Offer prices are in *currency units* (e.g. IRR, not Toman).
/// Currency/rounding and tax must be explicitly defined before real checkout.
/// </summary>
public sealed class CatalogPlanPriceOffer : AuditableEntity
{
    public CatalogPlanPriceOffer() : base(46) { }
    public Guid PlanVersionId { get; set; }
    public string BillingCycle { get; set; } = "";
    public string Currency { get; set; } = "IRR";
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? SaleStartsAtUtc { get; set; }
    public DateTime? SaleEndsAtUtc { get; set; }
}

/// <summary>
/// DB-authored plan-card description. Module/Capability linked rows must be
/// backed by the published version's real entitlements; Benefit is text only.
/// </summary>
public sealed class CatalogPlanDisplayItem : AuditableEntity
{
    public CatalogPlanDisplayItem() : base(47) { }
    public Guid PlanVersionId { get; set; }
    public string Kind { get; set; } = "";
    public Guid? ModuleId { get; set; }
    public Guid? CapabilityDefinitionId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsHighlighted { get; set; }
}
