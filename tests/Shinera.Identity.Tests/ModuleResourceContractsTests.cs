using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class ModuleResourceContractsTests
{
    [Fact]
    public void Current_sources_define_14_System_resources_and_separate_Tenant_core()
    {
        var registry = ModuleResourceContracts.Current();
        Assert.Empty(ModuleResourceContracts.Validate(registry));

        var system = registry.Where(x => x.ModuleCode == "system").ToArray();
        Assert.Equal(14, system.Length);
        Assert.All(system, x =>
            Assert.Equal(ModuleProductClass.PlatformCore, x.ProductClass));

        var tenant = Assert.Single(registry, x =>
            x.ModuleCode == "tenant-workspace");
        Assert.Equal("tenant-administration", tenant.ResourceCode);
        Assert.Equal(ModuleProductClass.TenantCoreAdministration, tenant.ProductClass);
        Assert.Equal(17, tenant.PermissionCodes.Count);
        Assert.DoesNotContain(registry, x =>
            x.ProductClass == ModuleProductClass.Commercial);
    }

    [Fact]
    public void Resource_is_a_Controller_group_with_its_own_permissions_not_a_page()
    {
        var valid = new[]
        {
            new ResourceContract("appointments", "bookings",
                ModuleProductClass.Commercial,
                ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"]),
            new ResourceContract("appointments", "calendar",
                ModuleProductClass.Commercial,
                ["CALENDAR.READ"])
        };
        Assert.Empty(ModuleResourceContracts.Validate(valid));
        Assert.Contains("bookings", valid.Select(x => x.ResourceCode));
    }

    [Fact]
    public void Duplicate_Resource_and_permission_ownership_must_fail_closed()
    {
        var definitions = new[]
        {
            new ResourceContract("appointments", "bookings",
                ModuleProductClass.Commercial, ["APPOINTMENTS.READ", "APPOINTMENTS.CREATE"]),
            new ResourceContract("appointments", "bookings",
                ModuleProductClass.Commercial, ["BOOKINGS.READ"]),
            new ResourceContract("appointments", "calendar",
                ModuleProductClass.Commercial, ["APPOINTMENTS.READ"])
        };
        var issues = ModuleResourceContracts.Validate(definitions);
        Assert.Contains(issues, x => x.Code == "duplicate_resource");
        Assert.Contains(issues, x => x.Code == "permission_has_multiple_resources");
    }

    [Fact]
    public void Commercial_Module_cannot_take_over_System_or_Tenant_core()
    {
        var wrong = new[]
        {
            new ResourceContract("system", "billing",
                ModuleProductClass.Commercial, ["BILLING.READ"]),
            new ResourceContract("tenant-workspace", "roles",
                ModuleProductClass.Commercial, ["TENANT.ROLES.READ"]),
            new ResourceContract("appointments", "payments",
                ModuleProductClass.PlatformCore, ["PAYMENTS.READ"])
        };
        var issues = ModuleResourceContracts.Validate(wrong);
        Assert.Equal(3, issues.Count(x => x.Code == "invalid_core_module_classification"));
    }

    [Fact]
    public void Mixed_Module_classifications_and_duplicate_permission_codes_are_invalid()
    {
        var definitions = new[]
        {
            new ResourceContract("appointments", "bookings",
                ModuleProductClass.Commercial,
                ["APPOINTMENTS.READ", "APPOINTMENTS.READ"]),
            new ResourceContract("appointments", "calendar",
                ModuleProductClass.InternalOnly, ["CALENDAR.READ"])
        };
        var issues = ModuleResourceContracts.Validate(definitions);
        Assert.Contains(issues, x => x.Code == "mixed_module_classification");
        Assert.Contains(issues, x => x.Code == "duplicate_permission_in_resource");
    }

    [Fact]
    public void Resource_specific_features_and_limits_require_correct_types()
    {
        var resources = Commercial();
        var capabilities = new[]
        {
            new CapabilityContract("appointments.recurring", "appointments", "bookings",
                CapabilityKind.Feature, CapabilityValueKind.Boolean, CapabilityMergePolicy.Override),
            new CapabilityContract("appointments.max-active", "appointments", "bookings",
                CapabilityKind.CountLimit, CapabilityValueKind.Integer, CapabilityMergePolicy.Maximum),
            new CapabilityContract("appointments.monthly", "appointments", "bookings",
                CapabilityKind.PeriodQuota, CapabilityValueKind.Integer, CapabilityMergePolicy.Additive),
            new CapabilityContract("appointments.confirm-mode", "appointments", null,
                CapabilityKind.AllowedModes, CapabilityValueKind.ModeSet,
                CapabilityMergePolicy.SetUnion)
        };
        var ops = new[]
        {
            new ResourceOperationContract("appointments", "bookings",
                "APPOINTMENTS.READ", "APPOINTMENTS.BOOKINGS.READ", []),
            new ResourceOperationContract("appointments", "bookings",
                "APPOINTMENTS.CREATE", "APPOINTMENTS.BOOKINGS.CREATE",
                ["appointments.recurring", "appointments.max-active", "appointments.monthly"])
        };
        Assert.Empty(ModuleResourceContracts.Validate(resources, capabilities, ops));
        Assert.Empty(ops[0].RequiredCapabilities);
        Assert.Equal(3, ops[1].RequiredCapabilities.Count);
    }

    [Fact]
    public void Cannot_bind_an_operation_to_foreign_Resource_or_capability()
    {
        var resources = Commercial();
        var capabilities = new[]
        {
            new CapabilityContract("appointments.max-active", "appointments", "bookings",
                CapabilityKind.CountLimit, CapabilityValueKind.Integer, CapabilityMergePolicy.Maximum),
            new CapabilityContract("staff.max-active", "staff", "members",
                CapabilityKind.CountLimit, CapabilityValueKind.Integer, CapabilityMergePolicy.Maximum)
        };
        var ops = new[]
        {
            new ResourceOperationContract("appointments", "calendar",
                "APPOINTMENTS.CREATE", "APPOINTMENTS.CALENDAR.CREATE",
                ["appointments.max-active", "staff.max-active"])
        };
        var issues = ModuleResourceContracts.Validate(resources, capabilities, ops);
        Assert.Contains(issues, x => x.Code == "capability_module_missing");
        Assert.Contains(issues, x => x.Code == "capability_resource_missing");
        Assert.Contains(issues, x => x.Code == "operation_permission_owner_mismatch");
        Assert.Contains(issues, x => x.Code == "operation_capability_wrong_owner");
    }

    [Fact]
    public void Invalid_limits_and_duplicate_operation_keys_are_blocked()
    {
        var resources = Commercial();
        var caps = new[]
        {
            new CapabilityContract("appointments.monthly", "appointments", "bookings",
                CapabilityKind.PeriodQuota, CapabilityValueKind.Boolean, CapabilityMergePolicy.Additive),
            new CapabilityContract("appointments.recurring", "appointments", "bookings",
                CapabilityKind.Feature, CapabilityValueKind.Boolean, CapabilityMergePolicy.Additive)
        };
        var ops = new[]
        {
            new ResourceOperationContract("appointments", "bookings",
                "APPOINTMENTS.CREATE", "APPOINTMENTS.CREATE", ["appointments.monthly"]),
            new ResourceOperationContract("appointments", "bookings",
                "APPOINTMENTS.READ", "APPOINTMENTS.CREATE", ["appointments.recurring"])
        };
        var issues = ModuleResourceContracts.Validate(resources, caps, ops);
        Assert.Contains(issues, x => x.Code == "capability_value_type_mismatch");
        Assert.Contains(issues, x => x.Code == "capability_merge_policy_invalid");
        Assert.Contains(issues, x => x.Code == "invalid_or_duplicate_operation_key");
    }

    private static ResourceContract[] Commercial() =>
    [
        new("appointments", "bookings", ModuleProductClass.Commercial,
            ["APPOINTMENTS.CREATE", "APPOINTMENTS.READ"]),
        new("appointments", "calendar", ModuleProductClass.Commercial,
            ["CALENDAR.READ"])
    ];
}
