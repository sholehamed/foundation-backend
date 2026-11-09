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
    public const string GroupsCreate = "TENANT.GROUPS.CREATE";
    public const string GroupsUpdate = "TENANT.GROUPS.UPDATE";
    public const string GroupsChangeState = "TENANT.GROUPS.CHANGE-STATE";
    public const string GroupsAssignMembers = "TENANT.GROUPS.ASSIGN-MEMBERS";
    public const string GroupsAssignRoles = "TENANT.GROUPS.ASSIGN-ROLES";
    public const string BranchesManage = "TENANT.BRANCHES.MANAGE";
    public const string RolesAssignPermissions = "TENANT.ROLES.ASSIGN-PERMISSIONS";
    public const string RolesCreate = "TENANT.ROLES.CREATE";
    public const string RolesUpdate = "TENANT.ROLES.UPDATE";
    public const string RolesChangeState = "TENANT.ROLES.CHANGE-STATE";
    public const string MembersAssignRoles = "TENANT.MEMBERS.ASSIGN-ROLES";
    public const string MembersCreate = "TENANT.MEMBERS.CREATE";
    public const string MembersInvite = "TENANT.MEMBERS.INVITE";
    public const string MembersIssueTempPassword = "TENANT.MEMBERS.ISSUE-TEMP-PASSWORD";

    public sealed record Definition(string Key, string Kind, string Route, string Name, string UiKey);
    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new(MembersRead, "PAGE", "/dashboard/members", "View tenant members", "tenant.workspace.members"),
        new(RolesRead, "PAGE", "/dashboard/roles", "View tenant roles", "tenant.workspace.roles"),
        new(GroupsRead, "PAGE", "/dashboard/groups", "View tenant groups", "tenant.workspace.groups"),
        new(GroupsCreate, "ACTION", "/dashboard/groups", "Create Tenant groups", "tenant.workspace.groups.create"),
        new(GroupsUpdate, "ACTION", "/dashboard/groups", "Update Tenant group metadata", "tenant.workspace.groups.update"),
        new(GroupsChangeState, "ACTION", "/dashboard/groups", "Activate/deactivate Tenant groups", "tenant.workspace.groups.change-state"),
        new(GroupsAssignMembers, "ACTION", "/dashboard/groups", "Add/remove ordinary members to Tenant groups", "tenant.workspace.groups.assign-members"),
        new(GroupsAssignRoles, "ACTION", "/dashboard/groups", "Assign ordinary Tenant roles to groups", "tenant.workspace.groups.assign-roles"),
        new(BranchesManage, "ACTION", "/dashboard/branches", "Create/update tenant branches", "tenant.workspace.branches.manage"),
        new(RolesAssignPermissions, "ACTION", "/dashboard/roles", "Assign tenant role permissions", "tenant.workspace.roles.assign-permissions"),
        new(RolesCreate, "ACTION", "/dashboard/roles", "Create tenant roles", "tenant.workspace.roles.create"),
        new(RolesUpdate, "ACTION", "/dashboard/roles", "Update tenant roles", "tenant.workspace.roles.update"),
        new(RolesChangeState, "ACTION", "/dashboard/roles", "Activate/deactivate tenant roles", "tenant.workspace.roles.change-state"),
        new(MembersAssignRoles, "ACTION", "/dashboard/members", "Assign ordinary Tenant roles to members", "tenant.workspace.members.assign-roles"),
        new(MembersCreate, "ACTION", "/dashboard/members", "Create inactive local Tenant members", "tenant.workspace.members.create"),
        new(MembersInvite, "ACTION", "/dashboard/members", "Send a single-use invite to inactive Tenant member", "tenant.workspace.members.invite"),
        new(MembersIssueTempPassword, "ACTION", "/dashboard/members", "Issue a short-lived one-time staff password for in-person handoff", "tenant.workspace.members.issue-temp-password")
    ];

    public static bool IsKnownKey(string key) =>
        Definitions.Any(x => StringComparer.Ordinal.Equals(x.Key, key));
}
