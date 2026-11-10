using Domain.Sharedkernel.Util;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence;

/// <summary>
/// Stable identity bootstrap values only. Do NOT add menu, page, permission,
/// API, role-grant or demo-Tenant arrays here: those old, commented-out lists
/// were never applied and disagreed with the live Angular/System registry.
///
/// Authoritative definitions:
/// - System: FeatureRegistryDefinitions.Features and UiElements
/// - Tenant: TenantWorkspacePermissionCatalog.Definitions
/// - Tenant menu routes: TenantWorkspaceNavigationService.RoutePermissions
///
/// The only shared identity is the historical Master Tenant ID. Never change
/// the discriminator/sequence without a reviewed data migration.
/// </summary>
public static class DefaultSeedData
{
    // Old DefaultSeedData.Entities.Tenant was enum ordinal 13.
    // The ID is deliberately unchanged so existing master-scoped roles,
    // grants, tenants and audit history keep their original storage scope.
    private const ushort MasterTenantEntityType = 13;
    private const ulong MasterTenantSequence = 1;

    public static Guid MasterTenantId =>
        StructuredGuidV7.CreateDeterministic(MasterTenantEntityType, MasterTenantSequence);

    // Return a FRESH EF entity per seed call, never reuse a static mutable
    // tracked Tenant instance across different DbContext lifetimes.
    public static Tenant MasterTenant => CreateMasterTenant();

    public static Tenant CreateMasterTenant() =>
        new(MasterTenantId, null, "Master Tenant", "shinera", "shinera.ir", true);

    public const string SuperAdminRoleName = "superadmin";
    public const string DevelopmentAdminUserName = "admin";

    // Only passed to the Development-only account seeder. Production must
    // never manufacture a known-password administrator account.
    public static User CreateDevelopmentAdmin() =>
        new(null, DevelopmentAdminUserName, "admin@master.ir",
            "admin", "admin", UserType.System);
}
