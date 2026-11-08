using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantRoleLifecycleValidationException(string message) : Exception(message);
public sealed class TenantRoleLifecycleConflictException(string message) : Exception(message);

/// <summary>
/// Server-authenticated local Owner-only role lifecycle. No tenant/actor/scope
/// comes from the request, and role metadata writes are atomically audited.
/// </summary>
public static class TenantRoleLifecycleWriter
{
    public sealed record CreateRequest(string? Name, string? Description);
    public sealed record UpdateRequest(string? Name, string? Description, string? ExpectedVersion);
    public sealed record ChangeStateRequest(bool IsActive, string? ExpectedVersion);
    public sealed record RoleView(
        Guid Id, string Name, string? Description, bool IsActive, bool IsOwner, string Version);

    private static bool IsReserved(string normalized) =>
        normalized is "OWNER" or "SUPERADMIN" or "SYSTEMADMIN";

    private static (string Name, string NormalizedName, string? Description) Normalize(
        string? name, string? description)
    {
        var trimmed = name?.Trim() ?? "";
        var normalized = trimmed.ToUpperInvariant();
        if (trimmed.Length is < 2 or > 100 || IsReserved(normalized) ||
            trimmed.Any(char.IsControl))
            throw new TenantRoleLifecycleValidationException(
                "Role name must have 2-100 characters and must not be reserved.");
        var detail = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (detail?.Length > 500 || (detail?.Any(char.IsControl) ?? false))
            throw new TenantRoleLifecycleValidationException(
                "Role description must have at most 500 characters.");
        return (trimmed, normalized, detail);
    }

    private static byte[] ParseVersion(string? text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text ?? "");
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new TenantRoleLifecycleValidationException("Expected SQL RowVersion is invalid.");
    }

    public static RoleView View(Role role) => new(
        role.Id, role.Name, role.Description, role.IsActive,
        role.NormalizedName == "OWNER",
        role.RowVersion is { Length: > 0 } ? Convert.ToBase64String(role.RowVersion) : "");

    public static async Task<bool> CanWriteAsync(
        ITenantContext current, IdentityDbContext db, string key,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() ||
            current.IsSystemContext || current.UserId is not Guid userId ||
            userId == Guid.Empty || current.ActiveTenantId is not Guid tenantId ||
            tenantId == Guid.Empty || current.HomeTenantId != tenantId ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;

        // Every lifecycle ACTION additionally demands Roles.Read and a live
        // local Owner. A direct UserPermission Deny defeats this gate.
        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                   current, db, TenantWorkspacePermissionCatalog.RolesRead, ct) &&
               await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, key, ct) &&
               await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    public static async Task<bool> CanModifyRoleAsync(
        ITenantContext current, IdentityDbContext db, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty || current.UserId is not Guid actor ||
            current.ActiveTenantId is not Guid tenantId ||
            !await db.Roles.AsNoTracking().AnyAsync(r =>
                r.Id == id && r.TenantId == tenantId &&
                r.Scope == RoleScope.Tenant && r.NormalizedName != "OWNER", ct))
            return false;

        if (await db.UserRoles.AsNoTracking().AnyAsync(ur =>
                ur.TenantId == tenantId && ur.UserId == actor && ur.RoleId == id, ct) ||
            await db.UserGroups.AsNoTracking().AnyAsync(ug =>
                ug.TenantId == tenantId && ug.UserId == actor &&
                ug.Group.TenantId == tenantId && ug.Group.IsActive &&
                db.GroupRoles.Any(gr =>
                    gr.TenantId == tenantId && gr.GroupId == ug.GroupId &&
                    gr.RoleId == id), ct))
            return false;
        return true;
    }

    public static async Task<RoleView> CreateAsync(
        ITenantContext current, IdentityDbContext db, CreateRequest? request,
        CancellationToken ct = default)
    {
        if (!await CanWriteAsync(
                current, db, TenantWorkspacePermissionCatalog.RolesCreate, ct))
            throw new UnauthorizedAccessException("Role create not authorized.");
        var fields = Normalize(request?.Name, request?.Description);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanWriteAsync(
                    current, db, TenantWorkspacePermissionCatalog.RolesCreate, ct))
                throw new UnauthorizedAccessException("Role create permission revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            if (await db.Roles.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.NormalizedName == fields.NormalizedName, ct))
                throw new TenantRoleLifecycleConflictException("Role name already exists.");

            // No grant, GroupRole or membership is created.
            var role = new Role(tenantId, fields.Name, fields.Description)
            {
                Scope = RoleScope.Tenant,
                IsActive = true
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            await AuditAsync(db, current.UserId!.Value, tenantId, role,
                "create", null, null, ct);
            await tx.CommitAsync(ct);
            return View(role);
        });
    }

    public static async Task<RoleView> UpdateAsync(
        ITenantContext current, IdentityDbContext db, Guid id, UpdateRequest? request,
        CancellationToken ct = default)
    {
        if (!await CanWriteAsync(current, db,
                TenantWorkspacePermissionCatalog.RolesUpdate, ct) ||
            !await CanModifyRoleAsync(current, db, id, ct))
            throw new UnauthorizedAccessException("Role edit not authorized.");
        var fields = Normalize(request?.Name, request?.Description);
        var version = ParseVersion(request?.ExpectedVersion);
        return await ChangeAsync(current, db, id,
            TenantWorkspacePermissionCatalog.RolesUpdate, version,
            (role) =>
            {
                if (role.Name == fields.Name && role.Description == fields.Description)
                    return false;
                role.Name = fields.Name;
                role.NormalizedName = fields.NormalizedName;
                role.Description = fields.Description;
                return true;
            }, "update", ct);
    }

    public static async Task<RoleView> ChangeStateAsync(
        ITenantContext current, IdentityDbContext db, Guid id,
        ChangeStateRequest? request, CancellationToken ct = default)
    {
        if (!await CanWriteAsync(current, db,
                TenantWorkspacePermissionCatalog.RolesChangeState, ct) ||
            !await CanModifyRoleAsync(current, db, id, ct))
            throw new UnauthorizedAccessException("Role state write not authorized.");
        if (request is null)
            throw new TenantRoleLifecycleValidationException("State request is required.");
        var version = ParseVersion(request.ExpectedVersion);
        return await ChangeAsync(current, db, id,
            TenantWorkspacePermissionCatalog.RolesChangeState, version,
            role =>
            {
                if (role.IsActive == request.IsActive) return false;
                role.IsActive = request.IsActive;
                return true;
            }, "state", ct);
    }

    private static async Task<RoleView> ChangeAsync(
        ITenantContext current, IdentityDbContext db, Guid id, string key,
        byte[] expected, Func<Role, bool> mutate, string action, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanWriteAsync(current, db, key, ct) ||
                !await CanModifyRoleAsync(current, db, id, ct))
                throw new UnauthorizedAccessException("Role lifecycle privilege revoked.");

            var tenantId = current.ActiveTenantId!.Value;
            var role = await db.Roles.SingleOrDefaultAsync(x =>
                x.Id == id && x.TenantId == tenantId &&
                x.Scope == RoleScope.Tenant && x.NormalizedName != "OWNER", ct);
            if (role is null)
                throw new UnauthorizedAccessException("Role is outside authorized scope.");
            // A request normally has a fresh DbContext, but always compare
            // against SQL's current RowVersion, not an earlier tracked role
            // (e.g. after FR-12B updated its CAS token on the same context).
            await db.Entry(role).ReloadAsync(ct);
            if (role.TenantId != tenantId || role.Scope != RoleScope.Tenant ||
                role.NormalizedName == "OWNER")
                throw new UnauthorizedAccessException("Role is outside authorized scope.");
            if (role.RowVersion is null || !role.RowVersion.SequenceEqual(expected))
                throw new TenantRoleLifecycleConflictException("Role changed; reload before saving.");

            var oldName = role.Name;
            var oldActive = role.IsActive;
            if (!mutate(role))
            {
                await tx.CommitAsync(ct);
                return View(role);
            }

            // EF embeds the loaded SQL RowVersion in the UPDATE predicate.
            // A concurrent mutation cannot overwrite the actor's reviewed state.
            await db.SaveChangesAsync(ct);
            await AuditAsync(db, current.UserId!.Value, tenantId, role,
                action, oldName, oldActive, ct);
            await tx.CommitAsync(ct);
            return View(role);
        });
    }

    private static Task AuditAsync(
        IdentityDbContext db, Guid actor, Guid tenantId, Role role,
        string action, string? previousName, bool? previousActive, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null ||
            action is not ("create" or "update" or "state"))
            throw new InvalidOperationException("Tenant role audit requires a SQL transaction.");
        var auditId = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantRoleChangeAuditEntries]
                ([Id],[TenantId],[ActorUserId],[RoleId],[Action],
                 [PreviousName],[CurrentName],[PreviousActive],[CurrentActive])
            VALUES ({auditId},{tenantId},{actor},{role.Id},{action},
                    {previousName},{role.Name},{previousActive},{role.IsActive})
            """, ct);
    }
}
