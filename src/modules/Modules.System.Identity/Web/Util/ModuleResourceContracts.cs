using System.Text.RegularExpressions;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-17A-2: Code-owned Module -> Resource -> Permission descriptions.
/// Resource means a logical Controller/EndpointGroup, NOT a domain entity,
/// a UI page, a plan, or an automatic grant. No database writes or runtime
/// commercial entitlement enforcement occur here.
/// </summary>
public enum ModuleProductClass
{
    PlatformCore,
    TenantCoreAdministration,
    Commercial,
    InternalOnly
}

public sealed record ResourceContract(
    string ModuleCode,
    string ResourceCode,
    ModuleProductClass ProductClass,
    IReadOnlyList<string> PermissionCodes);

public sealed record ResourceContractIssue(string Code, string Subject);

public enum CapabilityKind { Feature, CountLimit, PeriodQuota, AllowedModes }
public enum CapabilityValueKind { Boolean, Integer, ModeSet }
public enum CapabilityMergePolicy { Override, Additive, Maximum, SetUnion, DenyWins }

/// <summary>
/// A future PLAN may assign a value to a registered capability definition;
/// defining a capability does not grant a user, create a paid subscription,
/// or register executable database-supplied code.
/// </summary>
public sealed record CapabilityContract(
    string Code,
    string ModuleCode,
    string? ResourceCode,
    CapabilityKind Kind,
    CapabilityValueKind ValueKind,
    CapabilityMergePolicy MergePolicy);

/// <summary>
/// An operation (not an entire Resource) may require a specific typed
/// capability. READ operations must never automatically consume a CREATE
/// quota merely because they share a Controller/EndpointGroup.
/// </summary>
public sealed record ResourceOperationContract(
    string ModuleCode,
    string ResourceCode,
    string PermissionCode,
    string EndpointKey,
    IReadOnlyList<string> RequiredCapabilities);

public static class ModuleResourceContracts
{
    private static readonly Regex Code = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PermissionCode = new(
        "^[A-Z][A-Z0-9]*(?:[.-][A-Z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CapabilityCode = new(
        "^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EndpointKey = new(
        "^[A-Z][A-Z0-9]*(?:[.-][A-Z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The existing System registry and the 17 centrally owned Tenant admin
    /// definitions are authoritative. This projection never creates new
    /// ownership or assumes that the Tenant core module is commercially sold.
    /// </summary>
    public static IReadOnlyList<ResourceContract> Current() =>
    [
        .. FeatureRegistryDefinitions.Features.Select(feature =>
            new ResourceContract(feature.ModuleCode, feature.ResourceCode,
                ModuleProductClass.PlatformCore,
                feature.PermissionCodes.ToArray())),
        new ResourceContract(
            TenantPermissionCatalogSync.ModuleCode,
            TenantPermissionCatalogSync.ResourceCode,
            ModuleProductClass.TenantCoreAdministration,
            TenantWorkspacePermissionCatalog.Definitions
                .Select(definition => definition.Key).ToArray())
    ];

    public static IReadOnlyList<ResourceContractIssue> Validate(
        IEnumerable<ResourceContract> definitions,
        IEnumerable<CapabilityContract>? capabilities = null,
        IEnumerable<ResourceOperationContract>? operations = null)
    {
        var issues = new List<ResourceContractIssue>();
        var resources = definitions.ToArray();
        var capabilityItems = capabilities?.ToArray() ?? [];
        var operationItems = operations?.ToArray() ?? [];

        var resourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissionOwners = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var modules = new Dictionary<string, ModuleProductClass>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            var module = resource.ModuleCode ?? "";
            var code = resource.ResourceCode ?? "";
            var key = module + "/" + code;
            if (!Code.IsMatch(module) || !Code.IsMatch(code))
                issues.Add(new("invalid_resource_key", key));
            if (!resourceKeys.Add(key))
                issues.Add(new("duplicate_resource", key));

            if (modules.TryGetValue(module, out var previous) &&
                previous != resource.ProductClass)
                issues.Add(new("mixed_module_classification", module));
            modules[module] = resource.ProductClass;

            if ((module == "system" &&
                 resource.ProductClass != ModuleProductClass.PlatformCore) ||
                (module == TenantPermissionCatalogSync.ModuleCode &&
                 resource.ProductClass != ModuleProductClass.TenantCoreAdministration) ||
                (resource.ProductClass == ModuleProductClass.PlatformCore &&
                 module != "system") ||
                (resource.ProductClass == ModuleProductClass.TenantCoreAdministration &&
                 module != TenantPermissionCatalogSync.ModuleCode))
                issues.Add(new("invalid_core_module_classification", key));

            if (resource.PermissionCodes is null ||
                resource.PermissionCodes.Count == 0)
            {
                issues.Add(new("empty_permission_set", key));
                continue;
            }
            var ownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var permission in resource.PermissionCodes)
            {
                var p = permission ?? "";
                if (!PermissionCode.IsMatch(p))
                    issues.Add(new("invalid_permission_code", key + ":" + p));
                if (!ownCodes.Add(p))
                    issues.Add(new("duplicate_permission_in_resource", key + ":" + p));
                if (permissionOwners.TryGetValue(p, out var owner) &&
                    !string.Equals(owner, key, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new("permission_has_multiple_resources", p));
                else
                    permissionOwners[p] = key;
            }
        }

        var capabilityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registeredCapabilities = new Dictionary<string, CapabilityContract>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var capability in capabilityItems)
        {
            var key = capability.Code ?? "";
            var module = capability.ModuleCode ?? "";
            if (!CapabilityCode.IsMatch(key))
                issues.Add(new("invalid_capability_code", key));
            if (!capabilityKeys.Add(key))
                issues.Add(new("duplicate_capability_code", key));
            else
                registeredCapabilities[key] = capability;
            if (!Code.IsMatch(module) || !modules.ContainsKey(module))
                issues.Add(new("capability_module_missing", key));
            if (capability.ResourceCode is string resourceCode &&
                !resourceKeys.Contains(module + "/" + resourceCode))
                issues.Add(new("capability_resource_missing", key));

            var expected = capability.Kind switch
            {
                CapabilityKind.Feature => CapabilityValueKind.Boolean,
                CapabilityKind.CountLimit or CapabilityKind.PeriodQuota =>
                    CapabilityValueKind.Integer,
                CapabilityKind.AllowedModes => CapabilityValueKind.ModeSet,
                _ => (CapabilityValueKind)(-1)
            };
            if (capability.ValueKind != expected)
                issues.Add(new("capability_value_type_mismatch", key));
            if (!Enum.IsDefined(capability.MergePolicy) ||
                (capability.Kind == CapabilityKind.AllowedModes &&
                 capability.MergePolicy is not (
                     CapabilityMergePolicy.Override or CapabilityMergePolicy.SetUnion)) ||
                (capability.Kind == CapabilityKind.Feature &&
                 capability.MergePolicy is CapabilityMergePolicy.Additive))
                issues.Add(new("capability_merge_policy_invalid", key));
        }

        var operationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operationItems)
        {
            var key = (operation.EndpointKey ?? "").Trim();
            var owner = (operation.ModuleCode ?? "") + "/" +
                        (operation.ResourceCode ?? "");
            if (!EndpointKey.IsMatch(key) || !operationKeys.Add(key))
                issues.Add(new("invalid_or_duplicate_operation_key", key));
            if (!resourceKeys.Contains(owner) ||
                !permissionOwners.TryGetValue(operation.PermissionCode ?? "",
                    out var assignedOwner) ||
                !string.Equals(owner, assignedOwner, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("operation_permission_owner_mismatch", key));
            if (operation.RequiredCapabilities is null)
            {
                issues.Add(new("null_operation_capabilities", key));
                continue;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in operation.RequiredCapabilities)
            {
                var capabilityKey = name ?? "";
                if (!seen.Add(capabilityKey))
                    issues.Add(new("duplicate_operation_capability", key + ":" + capabilityKey));
                if (!registeredCapabilities.TryGetValue(capabilityKey, out var capability) ||
                    !string.Equals(capability.ModuleCode, operation.ModuleCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    (capability.ResourceCode is not null &&
                     !string.Equals(capability.ResourceCode, operation.ResourceCode,
                         StringComparison.OrdinalIgnoreCase)))
                    issues.Add(new("operation_capability_wrong_owner", key + ":" + capabilityKey));
            }
        }
        return issues;
    }
}
