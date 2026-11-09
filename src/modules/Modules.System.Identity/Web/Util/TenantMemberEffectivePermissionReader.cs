using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Tenant-local, Owner-only explanatory projection over the exact live
/// TenantWorkspacePermissionEvaluator. This is not a second authorization
/// engine: Allowed is always returned by the existing evaluator.
/// </summary>
public static class TenantMemberEffectivePermissionReader
{
    public sealed record Source(
        string Kind, Guid? RoleId, string? RoleName,
        Guid? GroupId, string? GroupName);

    public sealed record Item(
        string Code, string Name, string Kind, string Route,
        bool Registered, bool Allowed, string Decision,
        IReadOnlyList<Source> Sources, bool MoreSources);

    public sealed record Report(
        Guid UserId, string UserName, bool UserActive, bool IsOwner,
        IReadOnlyList<Item> Items);

    private const int MaxSourcesPerPermission = 20;

    public static async Task<bool> CanReadAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || current.IsSystemContext ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            current.UserId is not Guid actor || actor == Guid.Empty)
            return false;

        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.MembersRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    /// <summary>
    /// Known Tenant PAGE/ACTION catalog only. Do not claim endpoint usability:
    /// separate Owner, writable Tenant, target and feature flags may be
    /// mandatory beyond this raw live permission decision.
    /// </summary>
    public static async Task<Report?> ReadAsync(
        ITenantContext current, IdentityDbContext db, Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty || !await CanReadAsync(current, db, ct))
            return null;

        var tenantId = current.ActiveTenantId!.Value;
        var member = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId &&
                u.UserType == UserType.Tenant)
            .Select(u => new { u.Id, u.UserName, u.IsActive })
            .SingleOrDefaultAsync(ct);
        if (member is null) return null;

        var keys = TenantWorkspacePermissionCatalog.Definitions
            .Select(d => d.Key).ToArray();
        var definitions = await db.Permissions.AsNoTracking()
            .Where(p => keys.Contains(p.Code))
            .Select(p => new { p.Id, p.Code, p.IsActive }).ToListAsync(ct);
        var ids = definitions.Select(x => x.Id).ToArray();

        var overrides = await db.UserPermissions.AsNoTracking()
            .Where(up => up.TenantId == tenantId && up.UserId == userId &&
                ids.Contains(up.PermissionId))
            .Select(up => new { up.PermissionId, up.IsGranted }).ToListAsync(ct);

        // The same active, tenant-local role and group join conditions used
        // by TenantWorkspacePermissionEvaluator. Candidate sources never
        // override explicit Deny, disabled accounts or missing catalog rows.
        var roles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.TenantId == tenantId && ur.UserId == userId &&
                ur.Role.TenantId == tenantId && ur.Role.IsActive &&
                ur.Role.Scope == RoleScope.Tenant)
            .SelectMany(ur => db.RolePermissions
                .Where(rp => rp.TenantId == tenantId && rp.RoleId == ur.RoleId &&
                    ids.Contains(rp.PermissionId)),
                (ur, rp) => new {
                    rp.PermissionId, ur.RoleId, RoleName = ur.Role.Name
                })
            .ToListAsync(ct);

        var grouped = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.TenantId == tenantId && ug.UserId == userId &&
                ug.Group.TenantId == tenantId && ug.Group.IsActive)
            .SelectMany(ug => db.GroupRoles
                .Where(gr => gr.TenantId == tenantId && gr.GroupId == ug.GroupId &&
                    gr.Role.TenantId == tenantId && gr.Role.IsActive &&
                    gr.Role.Scope == RoleScope.Tenant),
                (ug, gr) => new {
                    ug.GroupId, GroupName = ug.Group.Name,
                    gr.RoleId, RoleName = gr.Role.Name
                })
            .SelectMany(x => db.RolePermissions
                .Where(rp => rp.TenantId == tenantId && rp.RoleId == x.RoleId &&
                    ids.Contains(rp.PermissionId)),
                (x, rp) => new {
                    rp.PermissionId, x.GroupId, x.GroupName,
                    x.RoleId, x.RoleName
                })
            .ToListAsync(ct);

        var isOwner = member.IsActive && await db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.TenantId == tenantId && ur.UserId == userId &&
                ur.Role.TenantId == tenantId && ur.Role.NormalizedName == "OWNER" &&
                ur.Role.IsActive && ur.Role.Scope != RoleScope.System, ct);

        var subject = new TenantContext();
        subject.InitializeTenant(userId, tenantId, [tenantId], [], tenantId);

        var items = new List<Item>(keys.Length);
        foreach (var def in TenantWorkspacePermissionCatalog.Definitions)
        {
            var matches = definitions.Where(x => x.Code == def.Key).ToArray();
            var registered = matches.Length == 1 && matches[0].IsActive;
            var selectedId = registered ? matches[0].Id : Guid.Empty;
            var direct = registered
                ? overrides.Where(o => o.PermissionId == selectedId).ToArray()
                : [];
            var fromRoles = registered
                ? roles.Where(r => r.PermissionId == selectedId).ToArray()
                : [];
            var fromGroups = registered
                ? grouped.Where(g => g.PermissionId == selectedId).ToArray()
                : [];

            // Multiple paths may support the same permission. Show evidence,
            // including overridden candidates, but the decision is authoritative.
            var sourceList = new List<Source>();
            if (direct.Length > 0)
                sourceList.Add(new Source(
                    direct.Length == 1
                        ? (direct[0].IsGranted ? "direct_allow" : "direct_deny")
                        : "direct_conflict", null, null, null, null));
            sourceList.AddRange(fromRoles.Select(r =>
                new Source("role", r.RoleId, r.RoleName, null, null)));
            sourceList.AddRange(fromGroups.Select(g =>
                new Source("group_role", g.RoleId, g.RoleName,
                    g.GroupId, g.GroupName)));
            if (isOwner)
                sourceList.Add(new Source("owner_fallback", null, null, null, null));

            // The source order is stable, and output remains bounded even
            // when a Tenant has many multi-role group memberships.
            var candidates = sourceList.Distinct()
                .OrderBy(s => s.Kind).ThenBy(s => s.GroupName)
                .ThenBy(s => s.RoleName).ThenBy(s => s.GroupId)
                .ThenBy(s => s.RoleId).ToArray();

            var allowed = await TenantWorkspacePermissionEvaluator.AllowsAsync(
                subject, db, def.Key, ct);
            string decision;
            if (!member.IsActive) decision = "account_inactive";
            else if (matches.Length > 1) decision = "duplicate_definition";
            else if (matches.Length == 1 && !matches[0].IsActive)
                decision = "definition_inactive";
            else if (direct.Length > 1) decision = "direct_conflict";
            else if (direct.Length == 1)
                decision = direct[0].IsGranted ? "direct_allow" : "direct_deny";
            else if (fromRoles.Length > 0) decision = "role";
            else if (fromGroups.Length > 0) decision = "group_role";
            else if (isOwner) decision = "owner_fallback";
            else decision = matches.Length == 0 ? "no_definition" : "not_granted";

            items.Add(new Item(
                def.Key, def.Name, def.Kind, def.Route,
                registered, allowed, decision,
                candidates.Take(MaxSourcesPerPermission).ToArray(),
                candidates.Length > MaxSourcesPerPermission));
        }

        return new Report(member.Id, member.UserName, member.IsActive,
            isOwner, items);
    }
}
