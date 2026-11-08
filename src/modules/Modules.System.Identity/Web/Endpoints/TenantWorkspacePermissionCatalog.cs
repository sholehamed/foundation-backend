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
    public const string RolesAssignPermissions = "TENANT.ROLES.ASSIGN-PERMISSIONS";
    public const string RolesCreate = "TENANT.ROLES.CREATE";
    public const string RolesUpdate = "TENANT.ROLES.UPDATE";
    public const string RolesChangeState = "TENANT.ROLES.CHANGE-STATE";

    public sealed record Definition(string Key, string Kind, string Route, string Name, string UiKey);
    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new(MembersRead, "PAGE", "/dashboard/members", "View tenant members", "tenant.workspace.members"),
        new(RolesRead, "PAGE", "/dashboard/roles", "View tenant roles", "tenant.workspace.roles"),
        new(GroupsRead, "PAGE", "/dashboard/groups", "View tenant groups", "tenant.workspace.groups"),
        new(BranchesManage, "ACTION", "/dashboard/branches", "Create/update tenant branches", "tenant.workspace.branches.manage"),
        new(RolesAssignPermissions, "ACTION", "/dashboard/roles", "Assign tenant role permissions", "tenant.workspace.roles.assign-permissions"),
        new(RolesCreate, "ACTION", "/dashboard/roles", "Create tenant roles", "tenant.workspace.roles.create"),
        new(RolesUpdate, "ACTION", "/dashboard/roles", "Update tenant roles", "tenant.workspace.roles.update"),
        new(RolesChangeState, "ACTION", "/dashboard/roles", "Activate/deactivate tenant roles", "tenant.workspace.roles.change-state")
    ];

    public static bool IsKnownKey(string key) =>
        Definitions.Any(x => StringComparer.Ordinal.Equals(x.Key, key));
}
