using Microsoft.AspNetCore.Builder;
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
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/admin-capabilities", GetAdministrationCapabilities)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members", GetMembers)
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
            return Results.Forbid();

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
            return Results.Forbid();

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
        if (!await CanManageBranchesAsync(current, db, ct)) return Results.Forbid();
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
        if (!await CanManageBranchesAsync(current, db, ct)) return Results.Forbid();
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

    public static async Task<IResult> GetCurrentUiAccess(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId || !current.ReadableTenantIds.Contains(tenantId))
            return Results.Forbid();
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
                or TenantWorkspacePermissionCatalog.RolesCreate
                or TenantWorkspacePermissionCatalog.RolesUpdate
                or TenantWorkspacePermissionCatalog.RolesChangeState
                or TenantWorkspacePermissionCatalog.MembersAssignRoles)
                allowed = allowed && current.WritableTenantIds.Contains(tenantId);
            // IAM writes also require a live local Owner, independent of
            // delegated read/group permissions.
            if (definition.Key is TenantWorkspacePermissionCatalog.RolesAssignPermissions
                or TenantWorkspacePermissionCatalog.RolesCreate
                or TenantWorkspacePermissionCatalog.RolesUpdate
                or TenantWorkspacePermissionCatalog.RolesChangeState
                or TenantWorkspacePermissionCatalog.MembersAssignRoles)
                allowed = allowed && await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(
                    current, db, ct);
            entries.Add(new UiAccessItem(
                definition.Key, definition.Kind, definition.Route, allowed));
        }
        return Results.Ok(new { entries });
    }

    public static async Task<IResult> GetAdministrationCapabilities(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return Results.Forbid();
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
            canViewRoles = roles,
            canViewGroups = groups,
            canManageBranches = await CanManageBranchesAsync(current, db, ct),
            canCreateRoles = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesCreate, ct),
            canUpdateRoles = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesUpdate, ct),
            canChangeRoleState = await TenantRoleLifecycleWriter.CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesChangeState, ct),
            canAssignMemberRoles = await TenantMemberRoleAssignmentWriter.CanManageAsync(
                current, db, ct)
        });
    }

    public sealed record DirectoryPage<T>(IReadOnlyList<T> Items, int Page, bool HasNext);
    public sealed record WorkspaceMemberDto(
        Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive);
    public sealed record WorkspaceRoleDto(
        Guid Id, string Name, string? Description, bool IsActive, bool IsOwner, string Version);
    public sealed record WorkspaceGroupDto(Guid Id, string Name, string? Description, bool IsActive);
    private const int DirectoryPageSize = 20;

    public static async Task<IResult> GetMembers(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.MembersRead, ct))
            return Results.Forbid();
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
            return Results.Forbid();
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
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
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
            return Results.Forbid();
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
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
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
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
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
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
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
            return Results.Forbid();
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
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
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

    public static async Task<IResult> GetGroups(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct))
            return Results.Forbid();
        if (page < 1 || page > 1000) return Results.BadRequest();

        var tenantId = current.ActiveTenantId!.Value;
        var rows = await db.Groups.AsNoTracking()
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize + 1)
            .Select(g => new WorkspaceGroupDto(g.Id, g.Name, g.Description, g.IsActive))
            .ToListAsync(ct);
        return Results.Ok(new DirectoryPage<WorkspaceGroupDto>(
            rows.Take(DirectoryPageSize).ToArray(), page, rows.Count > DirectoryPageSize));
    }
}
