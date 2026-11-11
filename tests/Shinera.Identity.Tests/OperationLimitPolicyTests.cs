using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class OperationLimitPolicyTests
{
    private static ResourceOperationContract Operation(params string[] required) =>
        new("appointments", "bookings", "APPOINTMENTS.CREATE",
            "APPOINTMENTS.BOOKINGS.CREATE", required);

    private static CapabilityContract Limit(string code, CapabilityKind kind) =>
        new(code, "appointments", "bookings", kind, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override);

    private static CapabilityContract Feature(string code) =>
        new(code, "appointments", "bookings", CapabilityKind.Feature,
            CapabilityValueKind.Boolean, CapabilityMergePolicy.Override);

    private static CapabilityContract Modes(string code) =>
        new(code, "appointments", null, CapabilityKind.AllowedModes,
            CapabilityValueKind.ModeSet, CapabilityMergePolicy.Override);

    private static ResolvedCatalogCapability Count(string code, long value) =>
        new(code, null, value, false, []);

    private static ResolvedCatalogCapability Unlimited(string code) =>
        new(code, null, null, true, []);

    private static OperationLimitDecision Check(
        ResourceOperationContract operation,
        CapabilityContract[] definitions,
        ResolvedCatalogCapability[] allowances,
        OperationUsageSample[] usage,
        string? mode = null) =>
        OperationLimitPolicy.Evaluate(operation, definitions, allowances, usage, mode);

    [Fact]
    public void No_bound_capability_means_no_usage_ceiling_not_an_authorization_grant()
    {
        var result = Check(Operation(), [], [], []);
        Assert.True(result.Permitted);
        Assert.Equal("no_usage_limit", result.Code);
        // No tenant, identity or subscription is supplied to this decision:
        // callers must separately enforce commercial entitlement and RBAC.
    }

    [Fact]
    public void Required_capability_missing_from_plan_is_not_implicit_unlimited()
    {
        var key = "appointments.max-active";
        var result = Check(Operation(key), [Limit(key, CapabilityKind.CountLimit)],
            [], []);
        Assert.False(result.Permitted);
        Assert.Equal("required_capability_missing", result.Code);
    }

    [Fact]
    public void Feature_requires_explicit_true()
    {
        var key = "appointments.recurring";
        var contract = Feature(key);
        var disabled = Check(Operation(key), [contract],
            [new(key, false, null, false, [])], []);
        Assert.Equal("feature_disabled", disabled.Code);

        var enabled = Check(Operation(key), [contract],
            [new(key, true, null, false, [])], []);
        Assert.True(enabled.Permitted);
    }

    [Theory]
    [InlineData(8, 2, 10, true)]
    [InlineData(8, 3, 10, false)]
    [InlineData(0, 1, 0, false)]
    [InlineData(0, 1, 1, true)]
    [InlineData(9, 1, 9, false)]
    public void Count_limit_enforces_growth_at_exact_boundary(
        long current, long change, long maximum, bool expected)
    {
        var key = "appointments.max-active";
        var result = Check(Operation(key), [Limit(key, CapabilityKind.CountLimit)],
            [Count(key, maximum)], [new(key, current, change)]);
        Assert.Equal(expected, result.Permitted);
        Assert.Equal(expected ? "capability_requirements_satisfied" : "limit_exceeded",
            result.Code);
    }

    [Fact]
    public void Explicit_unlimited_skips_usage_ceiling_even_when_no_usage_sample_exists()
    {
        var key = "appointments.max-active";
        var result = Check(Operation(key), [Limit(key, CapabilityKind.CountLimit)],
            [Unlimited(key)], []);
        Assert.True(result.Permitted);
    }

    [Fact]
    public void Count_cleanup_remains_possible_after_downgrade_below_current_usage()
    {
        var key = "appointments.max-active";
        var contracts = new[] { Limit(key, CapabilityKind.CountLimit) };
        var allowances = new[] { Count(key, 5) };
        Assert.True(Check(Operation(key), contracts, allowances,
            [new(key, 9, -1)]).Permitted);
        Assert.True(Check(Operation(key), contracts, allowances,
            [new(key, 9, 0)]).Permitted);
        Assert.Equal("limit_exceeded", Check(Operation(key), contracts, allowances,
            [new(key, 9, 1)]).Code);
    }

    [Fact]
    public void Finite_limit_requires_authoritative_usage_and_valid_value()
    {
        var key = "appointments.max-active";
        var definitions = new[] { Limit(key, CapabilityKind.CountLimit) };
        Assert.Equal("authoritative_usage_missing", Check(Operation(key),
            definitions, [Count(key, 2)], []).Code);
        Assert.Equal("invalid_capability_value", Check(Operation(key),
            definitions, [new(key, null, null, false, [])],
            [new(key, 0, 1)]).Code);
        Assert.Equal("invalid_capability_value", Check(Operation(key),
            definitions, [Count(key, -1)], [new(key, 0, 1)]).Code);
    }

    [Fact]
    public void Quota_requires_positive_usage_and_handles_overflow()
    {
        var key = "appointments.monthly";
        var definitions = new[] { Limit(key, CapabilityKind.PeriodQuota) };
        var allowance = new[] { Count(key, 500) };

        Assert.True(Check(Operation(key), definitions, allowance,
            [new(key, 499, 1)]).Permitted);
        Assert.Equal("limit_exceeded", Check(Operation(key), definitions,
            allowance, [new(key, 500, 1)]).Code);
        Assert.Equal("invalid_usage_change", Check(Operation(key), definitions,
            allowance, [new(key, 499, 0)]).Code);
        Assert.Equal("invalid_usage_change", Check(Operation(key), definitions,
            allowance, [new(key, long.MaxValue, 1)]).Code);
    }

    [Fact]
    public void Allowed_modes_require_an_explicit_supported_mode()
    {
        var key = "appointments.confirm-mode";
        var contract = Modes(key);
        var allowance = new ResolvedCatalogCapability(
            key, null, null, false, ["manual", "automatic"]);
        Assert.Equal("mode_required", Check(Operation(key), [contract],
            [allowance], []).Code);
        Assert.Equal("mode_unavailable", Check(Operation(key), [contract],
            [allowance], [], "unknown").Code);
        Assert.True(Check(Operation(key), [contract], [allowance],
            [], "manual").Permitted);
    }

    [Fact]
    public void Foreign_resource_and_duplicate_resolved_values_fail_closed()
    {
        var key = "appointments.max-active";
        var foreign = new CapabilityContract(key, "appointments", "calendar",
            CapabilityKind.CountLimit, CapabilityValueKind.Integer,
            CapabilityMergePolicy.Override);
        Assert.Equal("invalid_operation_capability_binding",
            Check(Operation(key), [foreign], [Count(key, 5)],
                [new(key, 0, 1)]).Code);

        Assert.Equal("invalid_plan_capabilities",
            Check(Operation(key), [Limit(key, CapabilityKind.CountLimit)],
                [Count(key, 5), Count(key, 7)], [new(key, 0, 1)]).Code);
    }

    [Fact]
    public void Every_required_capability_is_checked_independently()
    {
        var recurring = "appointments.recurring";
        var capacity = "appointments.max-active";
        var result = Check(Operation(recurring, capacity),
            [Feature(recurring), Limit(capacity, CapabilityKind.CountLimit)],
            [new(recurring, true, null, false, []), Count(capacity, 3)],
            [new(capacity, 3, 1)]);
        Assert.False(result.Permitted);
        Assert.Equal("limit_exceeded", result.Code);
        Assert.Equal(capacity, result.CapabilityCode);
    }
}
