using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

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
        app.MapPut($"/{backendPrefix.Trim('/')}/tenant/workspace/branches/{id:guid}", UpdateBranch)
            .RequireAuthorization()
            .WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/admin-capabilities", GetAdministrationCapabilities)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/members", GetMembers)
            .RequireAuthorization().WithTags("Tenant Workspace");
        app.MapGet($"/{backendPrefix.Trim('/')}/tenant/workspace/roles", GetRoles)
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
           await IsActiveLocalOwnerAsync(current, db, ct);

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

    // FR-09A: Until a tenant permission catalog is shipped, only a live, locally
    // assigned Owner role may read administrative directories. Mere membership,
    // tenant ReadScope/WriteScope, or an unrelated System role is not enough.
    private static async Task<bool> IsActiveLocalOwnerAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (current.IsSystemContext || current.ActiveTenantId is not Guid tenantId ||
            current.HomeTenantId != tenantId || current.UserId is not Guid actorId ||
            !current.ReadableTenantIds.Contains(tenantId))
            return false;

        if (!await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.IsActive, ct))
            return false;

        if (!await db.Users.AsNoTracking().AnyAsync(u =>
                u.Id == actorId && u.TenantId == tenantId &&
                u.UserType == UserType.Tenant && u.IsActive, ct))
            return false;

        // Public signup historically seeded Owner with RoleScope=0. Accept this
        // legacy value for this local role only; never accept a System-scoped role.
        return await db.UserRoles.AsNoTracking().AnyAsync(ur =>
            ur.TenantId == tenantId && ur.UserId == actorId &&
            ur.Role.TenantId == tenantId &&
            ur.Role.NormalizedName == "OWNER" && ur.Role.IsActive &&
            ur.Role.Scope != RoleScope.System, ct);
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
        return Results.Ok(new
        {
            canViewAdministration = await IsActiveLocalOwnerAsync(current, db, ct),
            canManageBranches = await CanManageBranchesAsync(current, db, ct)
        });
    }

    public sealed record DirectoryPage<T>(IReadOnlyList<T> Items, int Page, bool HasNext);
    public sealed record WorkspaceMemberDto(
        Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive);
    public sealed record WorkspaceRoleDto(Guid Id, string Name, string? Description, bool IsActive);
    public sealed record WorkspaceGroupDto(Guid Id, string Name, string? Description, bool IsActive);
    private const int DirectoryPageSize = 20;

    public static async Task<IResult> GetMembers(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsActiveLocalOwnerAsync(current, db, ct)) return Results.Forbid();
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

    public static async Task<IResult> GetRoles(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsActiveLocalOwnerAsync(current, db, ct)) return Results.Forbid();
        if (page < 1 || page > 1000) return Results.BadRequest();

        var tenantId = current.ActiveTenantId!.Value;
        var rows = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Scope != RoleScope.System)
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize + 1)
            .Select(r => new WorkspaceRoleDto(r.Id, r.Name, r.Description, r.IsActive))
            .ToListAsync(ct);
        return Results.Ok(new DirectoryPage<WorkspaceRoleDto>(
            rows.Take(DirectoryPageSize).ToArray(), page, rows.Count > DirectoryPageSize));
    }

    public static async Task<IResult> GetGroups(
        ITenantContext current, IdentityDbContext db, HttpContext http,
        int page = 1, CancellationToken ct = default)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsActiveLocalOwnerAsync(current, db, ct)) return Results.Forbid();
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
