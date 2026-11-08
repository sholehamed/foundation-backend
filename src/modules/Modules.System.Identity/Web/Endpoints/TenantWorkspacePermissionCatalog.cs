namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// An explicit, tenant-only PAGE/ACTION catalog. It is not a grant seeder:
/// definitions do not create Permissions, assignments or paid entitlements.
/// </summary>
public static class TenantWorkspacePermissionCatalog
{
    public const string MembersRead = "TENANT.MEMBERS.READ";
    public const string RolesRead = "TENANT.ROLES.READ";
    public const string GroupsRead = "TENANT.GROUPS.READ";
    public const string BranchesManage = "TENANT.BRANCHES.MANAGE";

    public sealed record Definition(string Key, string Kind, string Route, string Name);
    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new(MembersRead, "PAGE", "/dashboard/members", "View tenant members"),
        new(RolesRead, "PAGE", "/dashboard/roles", "View tenant roles"),
        new(GroupsRead, "PAGE", "/dashboard/groups", "View tenant groups"),
        new(BranchesManage, "ACTION", "/dashboard/branches", "Create/update tenant branches")
    ];

    public static bool IsKnownKey(string key) =>
        Definitions.Any(x => StringComparer.Ordinal.Equals(x.Key, key));
}
