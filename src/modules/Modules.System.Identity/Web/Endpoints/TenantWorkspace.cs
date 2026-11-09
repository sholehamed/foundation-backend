using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>Read the workspace owned by the authenticated tenant; never trust a client TenantId.</summary>
public static class TenantWorkspace
{
    // Authentication is already enforced by each endpoint's RequireAuthorization.
    // Explicit permission denials must be 403 for BOTH native bearer and the
    // opt-in BFF Cookie bridge. Results.Forbid() delegates to the default
    // OpenIddict bearer scheme and can issue 401 for a valid Cookie principal
    // that supplied no Authorization header (misleading re-login prompts).
    private static IResult DeniedAfterAuthentication() =>
        Results.StatusCode(StatusCodes.Status403Forbidden);

    public static void Map(WebApplication app, string backendPrefix)
    {
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace", Get)
            .RequireAuthorization()
            .WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/branches", GetBranches)
            .RequireAuthorization()
            .WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/branches", CreateBranch)
            .RequireAuthorization()
            .WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/branches/{{id:guid}}", UpdateBranch)
            .RequireAuthorization()
            .WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/ui-access/me", GetCurrentUiAccess)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/navigation/me", GetTenantNavigation)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/admin-capabilities", GetAdministrationCapabilities)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members", GetMembers)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members/audit", GetMemberAudit)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/members", CreatePendingMember)
            .RequireAuthorization()
            .RequireRateLimiting("tenant-member-create")
            .WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/members/with-initial-password", CreateMemberWithInitialPassword)
            .RequireAuthorization()
            .RequireRateLimiting("tenant-member-create")
            .WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/members/{{id:guid}}/invitations", InvitePendingMember)
            .RequireAuthorization()
            .RequireRateLimiting("tenant-member-invite")
            .WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/members/{{id:guid}}/temporary-password", IssueTemporaryPassword)
            .RequireAuthorization()
            .RequireRateLimiting("tenant-member-invite")
            .WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/public/tenant-login/resolve", ResolveTenantLogin)
            .AllowAnonymous()
            .RequireRateLimiting("tenant-first-login")
            .WithTags("Tenant Login");
        app.MapPost($"/{backendPrefix.Trim('/')}/public/tenant-first-login/start", StartFirstLogin)
            .AllowAnonymous()
            .RequireRateLimiting("tenant-first-login")
            .WithTags("First Login");
        app.MapPost($"/{backendPrefix.Trim('/')}/public/tenant-first-login/complete", CompleteFirstLogin)
            .AllowAnonymous()
            .RequireRateLimiting("tenant-first-login")
            .WithTags("First Login");
        app.MapPost($"/{backendPrefix.Trim('/')}/public/tenant-invitations/activate", ActivateInvitedMember)
            .AllowAnonymous()
            .RequireRateLimiting("tenant-invite-activate")
            .WithTags("Tenant Activation");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members/{{id:guid}}/effective-permissions", GetMemberEffectivePermissions)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members/{{id:guid}}/roles", GetMemberRoles)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/members/{{id:guid}}/roles", ReplaceMemberRoles)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/roles", GetRoles)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/roles", CreateRole)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/roles/{{id:guid}}", UpdateRole)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPatch($"/{backendPrefix.Trim('/')}/tenant/workspace/roles/{{id:guid}}/state", ChangeRoleState)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/roles/{{id:guid}}/permissions", GetRolePermissions)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/roles/{{id:guid}}/permissions", ReplaceRolePermissions)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/groups", GetGroups)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPost($"/{backendPrefix.Trim('/')}/tenant/workspace/groups", CreateGroup)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}", UpdateGroup)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPatch($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/state", ChangeGroupState)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/members", GetGroupMembers)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/members/{{userId:guid}}", ChangeGroupMember)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/audit", GetGroupAudit)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/roles", GetGroupRoles)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/groups/{{id:guid}}/roles/{{roleId:guid}}", ChangeGroupRole)
            .RequireAuthorization().WithTags("Tenant Workspace");
    }

    private static async Task<IResult> Get(
        ITenantContext current,
        IdentityDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return DeniedAfterAuthentication();

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId && t.IsActive)
            .Select(t => new { t.Id, t.Name, t.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null) return Results.NotFound();

        var profile = await db.BusinessProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => new {
                p.DisplayName, p.ActivityType, p.City, p.Address,
                p.RequestedPlanKey, p.RequestedBillingCycle
            })
            .SingleOrDefaultAsync(cancellationToken);
        var mainBranch = await db.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.IsMain)
            .Select(b => new { b.Id, b.Name, b.City, b.Address })
            .SingleOrDefaultAsync(cancellationToken);
        var branchesCount = await db.Branches.AsNoTracking()
            .CountAsync(b => b.TenantId == tenantId, cancellationToken);
        return Results.Ok(new {
            tenant, profile, mainBranch, branchesCount,
            // This is still a placeholder, NOT a confirmed paid subscription.
            subscriptionStatus = "pending_activation"
        });
    }
    /// <summary>Read-only owned branch summary; no tenant ID is accepted from the client.</summary>
    public sealed record WorkspaceBranchDto(
        Guid Id, string Name, string City, string Address, string? Phone, bool IsMain);

    public static async Task<IResult> GetBranches(
        ITenantContext current,
        IdentityDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return DeniedAfterAuthentication();

        if (!await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.IsActive, cancellationToken))
            return Results.NotFound();

        var branches = await db.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .OrderByDescending(b => b.IsMain)
            .ThenBy(b => b.Name)
            .Select(b => new WorkspaceBranchDto(
                b.Id, b.Name, b.City, b.Address, b.Phone, b.IsMain))
            .ToListAsync(cancellationToken);

        return Results.Ok(branches);
    }


    // FR-10A: Limited, owner-only branch writes. Tenant/owner identity is resolved
    // server-side and the explicit writable scope is required on every mutation.
    // Main-branch designation, deletion and reassignment are NOT supported.
    public sealed record BranchWriteRequest(string? Name, string? City, string? Address, string? Phone);

    private static async Task<bool> CanManageBranchesAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
        => current.ActiveTenantId is Guid active &&
           current.WritableTenantIds.Contains(active) &&
           await TenantWorkspacePermissionEvaluator.AllowsAsync(
               current, db, TenantWorkspacePermissionCatalog.BranchesManage, ct);

    private static Dictionary<string, string[]> ValidateBranch(BranchWriteRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["branch"] = ["اطلاعات شعبه الزامی است."];
            return errors;
        }
        void Required(string field, string? value, int limit)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit)
                errors[field] = [$"مقدار الزامی است و حداکثر {limit} کاراکتر دارد."];
        }
        Required("name", request.Name, 120);
        Required("city", request.City, 80);
        Required("address", request.Address, 400);
        if (request.Phone?.Trim().Length > 20)
            errors["phone"] = ["شماره تماس حداکثر ۲۰ کاراکتر دارد."];
        return errors;
    }

    private static WorkspaceBranchDto BranchDto(Branch branch) =>
        new(branch.Id, branch.Name, branch.City, branch.Address, branch.Phone, branch.IsMain);

    private static void ApplyBranchFields(Branch branch, BranchWriteRequest request)
    {
        branch.Name = request.Name!.Trim();
        branch.City = request.City!.Trim();
        branch.Address = request.Address!.Trim();
        branch.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
    }

    public static async Task<IResult> CreateBranch(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        BranchWriteRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await CanManageBranchesAsync(current, db, ct)) return DeniedAfterAuthentication();
        var errors = ValidateBranch(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Never accept TenantId/IsMain from a client body. The existing Main
        // branch is always preserved; this action creates a non-main branch.
        var branch = new Branch
        {
            TenantId = current.ActiveTenantId!.Value,
            IsMain = false
        };
        ApplyBranchFields(branch, request);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return Results.Ok(BranchDto(branch));
    }

    public static async Task<IResult> UpdateBranch(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, BranchWriteRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await CanManageBranchesAsync(current, db, ct)) return DeniedAfterAuthentication();
        if (id == Guid.Empty) return Results.NotFound();

        // The exact active tenant is a predicate, not merely an EF query filter.
        var tenantId = current.ActiveTenantId!.Value;
        var branch = await db.Branches
            .SingleOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId, ct);
        if (branch is null) return Results.NotFound();

        var errors = ValidateBranch(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        ApplyBranchFields(branch, request);
        // TenantId and IsMain never change even when editing the Main branch.
        await db.SaveChangesAsync(ct);
        return Results.Ok(BranchDto(branch));
    }

    // FR-11A: Permission decisions are live, scoped and individually evaluated.
    // The evaluated actor ID and tenant are never client-selected.
    public sealed record UiAccessItem(string Key, string Kind, string Route, bool Allowed);

    /// <summary>
    /// FR-14C-1: server-authorized dynamic navigation, matching the same
    /// current authenticated Tenant / live PAGE grants used by workspace APIs.
    /// Does not accept RoleId, TenantId or branch from query/body.
    /// </summary>
    public static async Task<IResult> GetTenantNavigation(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration configuration, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        var navigation = await TenantWorkspaceNavigationService.GetAsync(
            current, db, http.User, ct,
            ownedCatalogEnabled: configuration.GetValue<bool>(
                "Identity:TenantNavigation:OwnedCatalogEnabled"));
        return navigation is null ? DeniedAfterAuthentication() : Results.Ok(navigation);
    }

    public static async Task<IResult> GetCurrentUiAccess(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId || !current.ReadableTenantIds.Contains(tenantId))
            return DeniedAfterAuthentication();
        if (!await db.Tenants.AsNoTracking().AnyAsync(
                t => t.Id == tenantId && t.IsActive, ct))
            return Results.NotFound();

        var entries = new List<UiAccessItem>();
        foreach (var definition in TenantWorkspacePermissionCatalog.Definitions)
        {
            var allowed = await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, definition.Key, ct);
            // Writable scope is a separate mandatory bound for write actions.
            if (definition.Key is TenantWorkspacePermissionCatalog.BranchesManage
                or TenantWorkspacePermissionCatalog.RolesAssignPermissions
                or TenantWorkspacePermissionCatalog.GroupsCreate
                or TenantWorkspacePermissionCatalog.GroupsUpdate
                or TenantWorkspacePermissionCatalog.GroupsChangeState
                or TenantWorkspacePermissionCatalog.GroupsAssignMembers
                or TenantWorkspacePermissionCatalog.GroupsAssignRoles
                or TenantWorkspacePermissionCatalog.RolesCreate
                or TenantWorkspacePermissionCatalog.RolesUpdate
                or TenantWorkspacePermissionCatalog.RolesChangeState
                or TenantWorkspacePermissionCatalog.MembersAssignRoles
                or TenantWorkspacePermissionCatalog.MembersCreate
                or TenantWorkspacePermissionCatalog.MembersInvite
                or TenantWorkspacePermissionCatalog.MembersIssueTempPassword)
                allowed = allowed && current.WritableTenantIds.Contains(tenantId);
            // IAM writes also require a live local Owner, independent of
            // delegated read/group permissions.
            if (definition.Key is TenantWorkspacePermissionCatalog.RolesAssignPermissions
                or TenantWorkspacePermissionCatalog.GroupsCreate
                or TenantWorkspacePermissionCatalog.GroupsUpdate
                or TenantWorkspacePermissionCatalog.GroupsChangeState
                or TenantWorkspacePermissionCatalog.GroupsAssignMembers
                or TenantWorkspacePermissionCatalog.GroupsAssignRoles
                or TenantWorkspacePermissionCatalog.RolesCreate
                or TenantWorkspacePermissionCatalog.RolesUpdate
                or TenantWorkspacePermissionCatalog.RolesChangeState
                or TenantWorkspacePermissionCatalog.MembersAssignRoles
                or TenantWorkspacePermissionCatalog.MembersCreate
                or TenantWorkspacePermissionCatalog.MembersInvite
                or TenantWorkspacePermissionCatalog.MembersIssueTempPassword)
                allowed = allowed && await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(
                    current, db, ct);
            entries.Add(new UiAccessItem(
                definition.Key, definition.Kind, definition.Route, allowed));
        }
        return Results.Ok(new { entries });
    }

    public static async Task<IResult> GetAdministrationCapabilities(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration configuration, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return DeniedAfterAuthentication();
        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.IsActive, ct))
            return Results.NotFound();
        var members = await TenantWorkspacePermissionEvaluator.AllowsAsync(
            current, db, TenantWorkspacePermissionCatalog.MembersRead, ct);
        var roles = await TenantWorkspacePermissionEvaluator.AllowsAsync(
            current, db, TenantWorkspacePermissionCatalog.RolesRead, ct);
        var groups = await TenantWorkspacePermissionEvaluator.AllowsAsync(
            current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct);
        return Results.Ok(new
        {
            canViewAdministration = members || roles || groups,
            canViewMembers = members,
            canViewMemberAudit = members &&
                await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct),
            canViewMemberEffectivePermissions =
                await TenantMemberEffectivePermissionReader.CanReadAsync(current, db, ct),
            canViewRoles = roles,
            canViewGroups = groups,
            canViewGroupAudit = groups && members && roles &&
                await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct),
            canCreateGroups = await TenantGroupLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsCreate, ct),
            canUpdateGroups = await TenantGroupLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsUpdate, ct),
            canChangeGroupState = await TenantGroupLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsChangeState, ct),
            canAssignGroupMembers = await TenantGroupMemberAssignmentWriter.CanManageAsync(
                current, db, ct),
            canAssignGroupRoles = await TenantGroupRoleAssignmentWriter.CanManageAsync(
                current, db, ct),
            canManageBranches = await CanManageBranchesAsync(current, db, ct),
            canCreateRoles = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesCreate, ct),
            canUpdateRoles = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesUpdate, ct),
            canChangeRoleState = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesChangeState, ct),
            canAssignMemberRoles = await TenantMemberRoleAssignmentWriter.CanManageAsync(
                current, db, ct),
            canCreateMembers = await TenantPendingMemberWriter.CanCreateAsync(current, db, ct),
            canInviteMembers = await TenantMemberInvitationWriter.CanInviteAsync(current, db, ct),
            canIssueTemporaryPassword = configuration.GetValue<bool>(
                "Identity:TenantFirstLogin:Enabled") &&
                await TenantFirstLoginWriter.CanIssueAsync(current, db, ct)
        });
    }

    public sealed record DirectoryPage<T>(IReadOnlyList<T> Items, int Page, bool HasNext);
    public sealed record WorkspaceMemberDto(
        Guid Id, string UserName, string? Email, string? FirstName, string? LastName, bool IsActive);
    public sealed record WorkspaceRoleDto(
        Guid Id, string Name, string? Description, bool IsActive, bool IsOwner, string Version);
    public sealed record WorkspaceGroupDto(
        Guid Id, string Name, string? Description, bool IsActive, string Version = "");
    private const int DirectoryPageSize = 20;

    public static async Task<IResult> GetMembers(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct))
            return DeniedAfterAuthentication();
        if (page < 1 || page > 1000) return Results.BadRequest();

        var tenantId = current.ActiveTenantId!.Value;
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.UserType == UserType.Tenant)
            .OrderBy(u => u.UserName).ThenBy(u => u.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize + 1)
            .Select(u => new WorkspaceMemberDto(
                u.Id, u.UserName, u.Email, u.FirstName, u.LastName, u.IsActive))
            .ToListAsync(ct);
        return Results.Ok(new DirectoryPage<WorkspaceMemberDto>(
            rows.Take(DirectoryPageSize).ToArray(), page, rows.Count > DirectoryPageSize));
    }


    /// <summary>
    /// Explain live Tenant PAGE/ACTION permission decisions for a specific
    /// member, including direct overrides and active role/group paths.
    /// This report does not itself authorize privileged endpoint operations.
    /// </summary>
    public static async Task<IResult> GetMemberEffectivePermissions(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (id == Guid.Empty) return Results.BadRequest();
        if (!await TenantMemberEffectivePermissionReader.CanReadAsync(current, db, ct))
            return DeniedAfterAuthentication();
        var report = await TenantMemberEffectivePermissionReader.ReadAsync(
            current, db, id, ct);
        return report is null ? Results.NotFound() : Results.Ok(report);
    }

    /// <summary>
    /// Read-only, Owner-only member security audit. A normal member with
    /// MEMBERS.READ still cannot read staff lifecycle/role-change history.
    /// The SQL reader further restricts all rows and joins to the live tenant.
    /// </summary>
    public static async Task<IResult> GetMemberAudit(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, Guid? memberId = null, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 1000 || memberId == Guid.Empty)
            return Results.BadRequest();
        if (current.ActiveTenantId is not Guid tenantId ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct))
            return DeniedAfterAuthentication();
        if (!db.Database.IsSqlServer())
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var audit = await TenantMemberAuditReader.ReadAsync(
            db, tenantId, page, memberId, ct);
        return Results.Ok(audit);
    }

    // FR-13B-1: only create a disabled, non-login local member with a
    // random undisclosed password and append-only SQL audit. Invitation and
    // activation are separate reviewed operations, NOT simulated here.
    public static async Task<IResult> CreatePendingMember(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IPasswordHasher<User> hasher, TenantPendingMemberWriter.CreateRequest request,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await TenantPendingMemberWriter.CreateAsync(
                current, db, hasher, request, ct));
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantMemberCreateValidationException e)
        {
            return Results.Problem(statusCode: 400,
                title: "Invalid Tenant member", detail: e.Message);
        }
        catch (TenantMemberCreateConflictException e)
        {
            return Results.Problem(statusCode: 409,
                title: "Tenant member already exists", detail: e.Message);
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: 409, title: "Tenant member conflict");
        }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        {
            return Results.Problem(statusCode: 409,
                title: "Concurrent Tenant member creation");
        }
    }

    // FR-13B-3B: optional atomic creation + initial credential. Both explicit
    // Tenant Actions are rechecked inside the SQL transaction. The one-time
    // password is returned ONLY in this no-store, authenticated HTTPS response.
    public static async Task<IResult> CreateMemberWithInitialPassword(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration configuration, IPasswordHasher<User> hasher,
        TenantPendingMemberWriter.CreateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!http.Request.IsHttps)
            return Results.Problem(statusCode: 400, title: "HTTPS required");
        try
        {
            return Results.Ok(await TenantPendingMemberWriter.CreateWithInitialPasswordAsync(
                current, db, hasher, request, ct));
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantMemberCreateValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant member", detail: e.Message); }
        catch (TenantMemberCreateConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant member already exists", detail: e.Message); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant member conflict"); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Concurrent Tenant member creation"); }
    }

    // FR-13B-2: SMTP relay acceptance is not proof of inbox delivery.
    // No token enters an Owner API response. Missing relay -> fail closed 503.
    public static async Task<IResult> InvitePendingMember(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        ITenantInvitationSender sender, Guid id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var receipt = await TenantMemberInvitationWriter.InviteAsync(
                current, db, sender, id, ct);
            return Results.Ok(receipt);
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantInvitationTargetException) { return Results.NotFound(); }
        catch (TenantInvitationDeliveryException)
        { return Results.Problem(statusCode: 503, title: "Invitation delivery unavailable"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant invitation conflict"); }
        catch (SqlException e) when (e.Number is 1205 or 2601 or 2627 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant invitation conflict"); }
    }

    // Token possession is the ONLY activation authority, and the request uses
    // no cookies or authenticated principal. Enforce real HTTPS except explicit
    // Development loopback (disposable local test server).
    public static async Task<IResult> ActivateInvitedMember(
        HttpContext http, IHostEnvironment environment, IdentityDbContext db,
        IPasswordHasher<User> hasher,
        TenantMemberInvitationWriter.ActivateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!http.Request.IsHttps &&
            !(environment.IsDevelopment() &&
              http.Connection.RemoteIpAddress is { } remote &&
              IPAddress.IsLoopback(remote)))
            return Results.Problem(statusCode: 400, title: "HTTPS required");
        try
        {
            await TenantMemberInvitationWriter.ActivateAsync(db, hasher, request, ct);
            return Results.Ok(new { activated = true });
        }
        catch (TenantInvitationValidationException)
        { return Results.Problem(statusCode: 400, title: "Invalid or expired invitation"); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 400, title: "Invalid or expired invitation"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Account activation unavailable"); }
    }

    // A separate feature toggle, explicit registered ACTION and live Owner
    // permission protect the only endpoint that reveals the temporary secret.
    public static async Task<IResult> IssueTemporaryPassword(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        IConfiguration configuration, IPasswordHasher<User> hasher,
        Guid id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!http.Request.IsHttps) return Results.Problem(
            statusCode: 400, title: "HTTPS required");
        try
        {
            var result = await TenantFirstLoginWriter.IssueAsync(current, db, hasher, id, ct);
            // Exactly one response; no database, log or further GET can reveal it.
            return Results.Ok(result);
        }
        catch (FirstLoginDeniedException) { return DeniedAfterAuthentication(); }
        catch (FirstLoginTargetException) { return Results.NotFound(); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Temporary credential conflict"); }
        catch (SqlException e) when (e.Number is 1205 or 2601 or 2627 or 3960)
        { return Results.Problem(statusCode: 409, title: "Temporary credential conflict"); }
    }

    // Clients must use HTTPS on the exact tenant host. A valid first-login
    // challenge is deliberately NOT an app token/JWT or authenticated session.
    private static bool FirstLoginTransportAllowed(
        HttpContext http, IConfiguration configuration, IHostEnvironment environment,
        string? slug)
    {
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled") ||
            string.IsNullOrWhiteSpace(slug) ||
            !global::System.Text.RegularExpressions.Regex.IsMatch(slug,
                @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z") ||
            !http.Request.IsHttps)
            return false;
        var baseDomain = configuration["Tenant:WorkspaceBaseDomain"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseDomain)) return false;
        var expectedHost = slug + "." + baseDomain;
        return string.Equals(http.Request.Host.Host, expectedHost,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opt-in companion to the EXISTING Tenant login form. Not an OAuth
    /// endpoint and never signs in an inactive user. A verified temporary
    /// password yields only a short-lived change-only challenge. Unknown
    /// and ordinary credentials get the same continue_login response.
    /// The normal login/captcha handler still owns normal authentication.
    /// </summary>
    public static async Task<IResult> ResolveTenantLogin(
        HttpContext http, IConfiguration configuration, IHostEnvironment environment,
        IdentityDbContext db, IPasswordHasher<User> hasher,
        TenantFirstLoginWriter.StartRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!FirstLoginTransportAllowed(http, configuration, environment, request.TenantSlug))
            return Results.Problem(statusCode: 400, title: "Secure tenant host required");
        return Results.Ok(await TenantFirstLoginWriter.ResolveForLoginAsync(
            db, hasher, request, ct));
    }

    public static async Task<IResult> StartFirstLogin(
        HttpContext http, IConfiguration configuration, IHostEnvironment environment,
        IdentityDbContext db, IPasswordHasher<User> hasher,
        TenantFirstLoginWriter.StartRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!FirstLoginTransportAllowed(http, configuration, environment, request.TenantSlug))
            return Results.Problem(statusCode: 400, title: "Secure tenant host required");
        try
        {
            return Results.Ok(await TenantFirstLoginWriter.StartAsync(db, hasher, request, ct));
        }
        catch (FirstLoginValidationException)
        { return Results.Problem(statusCode: 401, title: "Invalid first-login credentials"); }
    }

    public static async Task<IResult> CompleteFirstLogin(
        HttpContext http, IConfiguration configuration, IHostEnvironment environment,
        IdentityDbContext db, IPasswordHasher<User> hasher,
        TenantFirstLoginWriter.CompleteRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!configuration.GetValue<bool>("Identity:TenantFirstLogin:Enabled"))
            return Results.NotFound();
        if (!FirstLoginTransportAllowed(http, configuration, environment, request.TenantSlug))
            return Results.Problem(statusCode: 400, title: "Secure tenant host required");
        try
        {
            await TenantFirstLoginWriter.CompleteAsync(db, hasher, request, ct);
            return Results.Ok(new { passwordChanged = true, requiresRelogin = true });
        }
        catch (FirstLoginValidationException)
        { return Results.Problem(statusCode: 400, title: "Invalid or expired password-change challenge"); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 400, title: "Invalid or expired password-change challenge"); }
    }

    // FR-12D: direct UserRole matrix for one active tenant; never an
    // effective-permission or cross-tenant membership read.
    public static async Task<IResult> GetMemberRoles(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return DeniedAfterAuthentication();
        var matrix = await TenantMemberRoleAssignmentWriter.ReadAsync(
            current, db, id, ct);
        return matrix is null ? Results.NotFound() : Results.Ok(matrix);
    }

    public static async Task<IResult> ReplaceMemberRoles(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, TenantMemberRoleAssignmentWriter.ReplaceRequest request,
        TrustedTenantWriteScope trustedWrites, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var version = await TenantMemberRoleAssignmentWriter.ReplaceAsync(
                current, db, id, request, ct, trustedWrites);
            return Results.Ok(new { version });
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantMemberRoleValidationException e)
        {
            return Results.Problem(statusCode: 400,
                title: "Invalid Tenant member role request", detail: e.Message);
        }
        catch (TenantMemberRoleConflictException e)
        {
            return Results.Problem(statusCode: 409,
                title: "Tenant member roles changed", detail: e.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: 409,
                title: "Tenant member role version conflict");
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: 409,
                title: "Tenant member role write conflict");
        }
        catch (SqlException e) when (e.Number is 1205 or 2601 or 2627 or 3960)
        {
            return Results.Problem(statusCode: 409,
                title: "Concurrent Tenant member role assignment");
        }
    }

    public static async Task<IResult> GetRoles(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return DeniedAfterAuthentication();
        if (page < 1 || page > 1000) return Results.BadRequest();

        var tenantId = current.ActiveTenantId!.Value;
        var rows = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Scope != RoleScope.System)
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize + 1)
            .Select(r => new { r.Id, r.Name, r.Description, r.IsActive,
                r.NormalizedName, r.RowVersion })
            .ToListAsync(ct);
        return Results.Ok(new DirectoryPage<WorkspaceRoleDto>(
            rows.Take(DirectoryPageSize)
                .Select(r => new WorkspaceRoleDto(r.Id, r.Name, r.Description, r.IsActive,
                    r.NormalizedName == "OWNER", r.RowVersion is { Length: > 0 }
                        ? Convert.ToBase64String(r.RowVersion) : ""))
                .ToArray(), page, rows.Count > DirectoryPageSize));
    }



    // FR-12C: write only role metadata/state, not assignments, from the live
    // Tenant Owner and matching explicit action grant; audit atomically.
    public static async Task<IResult> CreateRole(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        TenantRoleLifecycleWriter.CreateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await TenantRoleLifecycleWriter.CreateAsync(current, db, request, ct));
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantRoleLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant role", detail: e.Message); }
        catch (TenantRoleLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant role already exists", detail: e.Message); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant role changed during write"); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant role conflict"); }
    }

    public static async Task<IResult> UpdateRole(
        ITenantContext current, IdentityDbContext db, HttpContext http, Guid id,
        TenantRoleLifecycleWriter.UpdateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await TenantRoleLifecycleWriter.UpdateAsync(current, db, id, request, ct));
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantRoleLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant role", detail: e.Message); }
        catch (TenantRoleLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant role version conflict", detail: e.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Tenant role version conflict"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant role changed during write"); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant role conflict"); }
    }

    public static async Task<IResult> ChangeRoleState(
        ITenantContext current, IdentityDbContext db, HttpContext http, Guid id,
        TenantRoleLifecycleWriter.ChangeStateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            return Results.Ok(await TenantRoleLifecycleWriter.ChangeStateAsync(current, db, id, request, ct));
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantRoleLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant role", detail: e.Message); }
        catch (TenantRoleLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant role version conflict", detail: e.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Tenant role version conflict"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant role changed during write"); }
        catch (SqlException e) when (e.Number is 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant role concurrent change"); }
    }

    // FR-12A: A read-only, local-role direct-grant matrix. These booleans are
    // not a claim that the requesting user has a given effective permission.
    public sealed record TenantRolePermissionItem(
        string Code, string Name, string Kind, string Route, bool Registered, bool Granted);
    public sealed record TenantRolePermissionMatrix(
        Guid RoleId, string RoleName, IReadOnlyList<TenantRolePermissionItem> Permissions,
        string Version, bool CanEdit);

    public static async Task<IResult> GetRolePermissions(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return DeniedAfterAuthentication();
        if (id == Guid.Empty) return Results.NotFound();
        var tenantId = current.ActiveTenantId!.Value;
        var role = await db.Roles.AsNoTracking()
            .Where(x => x.Id == id && x.TenantId == tenantId &&
                        x.Scope != RoleScope.System)
            .Select(x => new { x.Id, x.Name, x.RowVersion, x.IsActive, x.Scope, x.NormalizedName })
            .SingleOrDefaultAsync(ct);
        if (role is null) return Results.NotFound();

        var codes = TenantWorkspacePermissionCatalog.Definitions.Select(x => x.Key).ToArray();
        var stored = await db.Permissions.AsNoTracking()
            .Where(x => codes.Contains(x.Code))
            .Select(x => new { x.Id, x.Code, x.IsActive })
            .ToListAsync(ct);
        var links = await db.RolePermissions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RoleId == id)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);
        var assigned = links.ToHashSet();
        var items = TenantWorkspacePermissionCatalog.Definitions.Select(def =>
        {
            var candidates = stored.Where(x => x.Code == def.Key).ToList();
            var p = candidates.Count == 1 && candidates[0].IsActive ? candidates[0] : null;
            return new TenantRolePermissionItem(
                def.Key, def.Name, def.Kind, def.Route, p is not null,
                p is not null && assigned.Contains(p.Id));
        }).ToArray();
        var eligible = items.All(x => x.Registered);
        var editable = eligible && await TenantRolePermissionGrantWriter.CanEditAsync(
            current, db, id, ct);
        return Results.Ok(new TenantRolePermissionMatrix(
            role.Id, role.Name, items,
            role.RowVersion is { Length: > 0 } ? Convert.ToBase64String(role.RowVersion) : "",
            editable));
    }


    // FR-12B: only explicit, reviewed Tenant PAGE/ACTION codes are writable.
    // The client supplies neither actor nor TenantId. Every mutation runs
    // behind server-side Owner, scoped SQL CAS and append-only audit.
    public static async Task<IResult> ReplaceRolePermissions(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, TenantRolePermissionGrantWriter.ReplaceRequest request,
        TrustedTenantWriteScope trustedWrites, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var version = await TenantRolePermissionGrantWriter.ReplaceAsync(
                current, db, id, request, ct, trustedWrites);
            return Results.Ok(new { version });
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantRoleGrantMissingException) { return Results.NotFound(); }
        catch (TenantRoleGrantValidationException error)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Tenant role permission request", detail: error.Message);
        }
        catch (TenantRoleGrantConflictException error)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Tenant role permissions changed", detail: error.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Tenant grant conflict; reload role permissions");
        }
        catch (DbUpdateException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Tenant grant state changed during save");
        }
        catch (SqlException error) when (error.Number is 1205 or 2601 or 2627 or 3960)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Concurrent Tenant grant change; reload and retry");
        }
    }

    // Group metadata/state ONLY. Privileged UserGroup/GroupRole links remain
    // fail-closed in TenantSaveChangesInterceptor until separately audited.
    public static async Task<IResult> CreateGroup(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        TenantGroupLifecycleWriter.CreateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await TenantGroupLifecycleWriter.CreateAsync(current, db, request, ct)); }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantGroupLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant Group", detail: e.Message); }
        catch (TenantGroupLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant Group already exists", detail: e.Message); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant Group create conflict"); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant Group SQL conflict"); }
    }

    public static async Task<IResult> UpdateGroup(
        ITenantContext current, IdentityDbContext db, HttpContext http, Guid id,
        TenantGroupLifecycleWriter.UpdateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await TenantGroupLifecycleWriter.UpdateAsync(current, db, id, request, ct)); }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantGroupLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant Group", detail: e.Message); }
        catch (TenantGroupLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant Group version conflict", detail: e.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Tenant Group concurrency conflict"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant Group write conflict"); }
        catch (SqlException e) when (e.Number is 2601 or 2627 or 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant Group SQL conflict"); }
    }

    public static async Task<IResult> ChangeGroupState(
        ITenantContext current, IdentityDbContext db, HttpContext http, Guid id,
        TenantGroupLifecycleWriter.ChangeStateRequest request, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(await TenantGroupLifecycleWriter.ChangeStateAsync(current, db, id, request, ct)); }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantGroupLifecycleValidationException e)
        { return Results.Problem(statusCode: 400, title: "Invalid Tenant Group", detail: e.Message); }
        catch (TenantGroupLifecycleConflictException e)
        { return Results.Problem(statusCode: 409, title: "Tenant Group version conflict", detail: e.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Tenant Group concurrency conflict"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Tenant Group state conflict"); }
        catch (SqlException e) when (e.Number is 1205 or 3960)
        { return Results.Problem(statusCode: 409, title: "Tenant Group SQL conflict"); }
    }

    /// <summary>
    /// Local Owner-only, read-only history and historical relationship triage.
    /// Requires all three relevant live PAGE grants, not simply Group write.
    /// </summary>
    public static async Task<IResult> GetGroupAudit(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (id == Guid.Empty || page is < 1 or > 1000) return Results.BadRequest();
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct))
            return DeniedAfterAuthentication();
        if (!db.Database.IsSqlServer())
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        var group = await db.Groups.AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.Id == id)
            .Select(g => new { g.Id, g.Name })
            .SingleOrDefaultAsync(ct);
        if (group is null) return Results.NotFound();
        return Results.Ok(await TenantGroupSecurityAuditReader.ReadAsync(
            db, tenantId, group.Id, group.Name, page, ct));
    }

    /// <summary>Only Tenant Groups + Roles PAGE permissions expose the role matrix.</summary>
    public static async Task<IResult> GetGroupRoles(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 1000) return Results.BadRequest();
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesRead, ct))
            return DeniedAfterAuthentication();
        var matrix = await TenantGroupRoleAssignmentWriter.ReadAsync(
            current, db, id, page, ct);
        return matrix is null ? Results.NotFound() : Results.Ok(matrix);
    }

    public static async Task<IResult> ChangeGroupRole(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, Guid roleId, TenantGroupRoleAssignmentWriter.ChangeRequest request,
        TrustedTenantWriteScope trustedWrites, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var version = await TenantGroupRoleAssignmentWriter.ChangeAsync(
                current, db, id, roleId, request, trustedWrites, ct);
            return Results.Ok(new { version });
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantGroupRoleValidationException error)
        { return Results.Problem(statusCode: 400,
            title: "Invalid Group role request", detail: error.Message); }
        catch (TenantGroupRoleConflictException error)
        { return Results.Problem(statusCode: 409,
            title: "Group role conflict", detail: error.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Group role concurrent edit"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Group role storage conflict"); }
        catch (SqlException error) when (error.Number is 1205 or 2601 or 2627 or 3960)
        { return Results.Problem(statusCode: 409,
            title: "Group role concurrent SQL operation"); }
    }

    public static async Task<IResult> GetGroupMembers(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 1000) return Results.BadRequest();
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct))
            return DeniedAfterAuthentication();
        var matrix = await TenantGroupMemberAssignmentWriter.ReadAsync(
            current, db, id, page, ct);
        return matrix is null ? Results.NotFound() : Results.Ok(matrix);
    }

    public static async Task<IResult> ChangeGroupMember(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        Guid id, Guid userId, TenantGroupMemberAssignmentWriter.ChangeRequest request,
        TrustedTenantWriteScope trustedWrites, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var version = await TenantGroupMemberAssignmentWriter.ChangeAsync(
                current, db, id, userId, request, trustedWrites, ct);
            return Results.Ok(new { version });
        }
        catch (UnauthorizedAccessException) { return DeniedAfterAuthentication(); }
        catch (TenantGroupMemberValidationException error)
        { return Results.Problem(statusCode: 400,
            title: "Invalid Group member request", detail: error.Message); }
        catch (TenantGroupMemberConflictException error)
        { return Results.Problem(statusCode: 409,
            title: "Group membership conflict", detail: error.Message); }
        catch (DbUpdateConcurrencyException)
        { return Results.Problem(statusCode: 409, title: "Group membership concurrent edit"); }
        catch (DbUpdateException)
        { return Results.Problem(statusCode: 409, title: "Group membership storage conflict"); }
        catch (SqlException error) when (error.Number is 1205 or 2601 or 2627 or 3960)
        { return Results.Problem(statusCode: 409,
            title: "Group membership concurrent SQL operation"); }
    }

    public static async Task<IResult> GetGroups(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct))
            return DeniedAfterAuthentication();
        if (page < 1 || page > 1000) return Results.BadRequest();

        var tenantId = current.ActiveTenantId!.Value;
        var rows = await db.Groups.AsNoTracking()
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize + 1)
            .Select(g => new { g.Id, g.Name, g.Description, g.IsActive, g.RowVersion })
            .ToListAsync(ct);
        return Results.Ok(new DirectoryPage<WorkspaceGroupDto>(
            rows.Take(DirectoryPageSize)
                .Select(g => new WorkspaceGroupDto(g.Id, g.Name, g.Description,
                    g.IsActive, g.RowVersion is { Length: > 0 }
                        ? Convert.ToBase64String(g.RowVersion) : ""))
                .ToArray(), page, rows.Count > DirectoryPageSize));
    }
}
