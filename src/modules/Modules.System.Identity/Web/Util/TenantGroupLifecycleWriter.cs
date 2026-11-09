using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantGroupLifecycleValidationException(string message) : Exception(message);
public sealed class TenantGroupLifecycleConflictException(string message) : Exception(message);

/// <summary>
/// Tenant Group metadata/state only. Does NOT create UserGroups, GroupRoles,
/// UserPermissions or any grant: those require separate audited grant APIs.
/// </summary>
public static class TenantGroupLifecycleWriter
{
    public sealed record CreateRequest(string? Name, string? Description);
    public sealed record UpdateRequest(string? Name, string? Description, string? ExpectedVersion);
    public sealed record ChangeStateRequest(bool IsActive, string? ExpectedVersion);
    public sealed record GroupView(
        Guid Id, string Name, string? Description, bool IsActive, string Version);

    public static GroupView View(Group value) => new(
        value.Id, value.Name, value.Description, value.IsActive,
        value.RowVersion is { Length: > 0 } ? Convert.ToBase64String(value.RowVersion) : "");

    private static (string Name, string NormalizedName, string? Description) Normalize(
        string? name, string? description)
    {
        var trimmed = name?.Trim() ?? "";
        var normalized = trimmed.ToUpperInvariant();
        if (trimmed.Length is < 2 or > 100 || trimmed.Any(char.IsControl) ||
            normalized is "OWNER" or "SUPERADMIN" or "SYSTEMADMIN")
            throw new TenantGroupLifecycleValidationException(
                "Group name must be 2-100 characters, not reserved or control characters.");
        var detail = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (detail?.Length > 500 || (detail?.Any(char.IsControl) ?? false))
            throw new TenantGroupLifecycleValidationException(
                "Description must have at most 500 characters.");
        return (trimmed, normalized, detail);
    }

    private static byte[] ParseVersion(string? text)
    {
        try
        {
            var data = Convert.FromBase64String(text ?? "");
            if (data.Length == 8) return data;
        }
        catch (FormatException) { }
        throw new TenantGroupLifecycleValidationException("Expected SQL RowVersion is invalid.");
    }

    public static async Task<bool> CanWriteAsync(
        ITenantContext current, IdentityDbContext db, string action, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() ||
            current.IsSystemContext || current.UserId is not Guid userId ||
            userId == Guid.Empty || current.ActiveTenantId is not Guid tenantId ||
            tenantId == Guid.Empty || current.HomeTenantId != tenantId ||
            !current.WritableTenantIds.Contains(tenantId))
            return false;
        return await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, TenantWorkspacePermissionCatalog.GroupsRead, ct) &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(
                current, db, action, ct) &&
            await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
    }

    public static async Task<GroupView> CreateAsync(
        ITenantContext current, IdentityDbContext db,
        CreateRequest? request, CancellationToken ct = default)
    {
        if (!await CanWriteAsync(current, db, TenantWorkspacePermissionCatalog.GroupsCreate, ct))
            throw new UnauthorizedAccessException("Tenant Group create not authorized.");
        var input = Normalize(request?.Name, request?.Description);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanWriteAsync(current, db, TenantWorkspacePermissionCatalog.GroupsCreate, ct))
                throw new UnauthorizedAccessException("Tenant Group create revoked.");
            var tenantId = current.ActiveTenantId!.Value;
            if (await db.Groups.AsNoTracking().AnyAsync(g =>
                    g.TenantId == tenantId && g.NormalizedName == input.NormalizedName, ct))
                throw new TenantGroupLifecycleConflictException("Tenant Group already exists.");
            var group = new Group(Guid.NewGuid())
            {
                TenantId = tenantId,
                Name = input.Name,
                NormalizedName = input.NormalizedName,
                Description = input.Description,
                IsActive = true
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync(ct);
            await AuditAsync(db, tenantId, current.UserId!.Value, group,
                "create", null, null, ct);
            await tx.CommitAsync(ct);
            return View(group);
        });
    }

    public static async Task<GroupView> UpdateAsync(
        ITenantContext current, IdentityDbContext db, Guid id,
        UpdateRequest? request, CancellationToken ct = default)
    {
        var input = Normalize(request?.Name, request?.Description);
        var version = ParseVersion(request?.ExpectedVersion);
        return await ChangeAsync(current, db, id, version,
            TenantWorkspacePermissionCatalog.GroupsUpdate, "update",
            g =>
            {
                if (g.Name == input.Name && g.Description == input.Description)
                    return false;
                g.Name = input.Name;
                g.NormalizedName = input.NormalizedName;
                g.Description = input.Description;
                return true;
            }, ct);
    }

    public static async Task<GroupView> ChangeStateAsync(
        ITenantContext current, IdentityDbContext db, Guid id,
        ChangeStateRequest? request, CancellationToken ct = default)
    {
        if (request is null)
            throw new TenantGroupLifecycleValidationException("State request required.");
        var version = ParseVersion(request.ExpectedVersion);
        return await ChangeAsync(current, db, id, version,
            TenantWorkspacePermissionCatalog.GroupsChangeState, "state",
            g =>
            {
                if (g.IsActive == request.IsActive) return false;
                g.IsActive = request.IsActive;
                return true;
            }, ct);
    }

    private static async Task<GroupView> ChangeAsync(
        ITenantContext current, IdentityDbContext db, Guid id, byte[] expected,
        string actionKey, string auditAction, Func<Group, bool> mutation,
        CancellationToken ct)
    {
        if (id == Guid.Empty || !await CanWriteAsync(current, db, actionKey, ct))
            throw new UnauthorizedAccessException("Tenant Group write not authorized.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            if (!await CanWriteAsync(current, db, actionKey, ct))
                throw new UnauthorizedAccessException("Tenant Group permission revoked.");
            var tenantId = current.ActiveTenantId!.Value;
            var group = await db.Groups.SingleOrDefaultAsync(g =>
                g.Id == id && g.TenantId == tenantId, ct);
            if (group is null)
                throw new UnauthorizedAccessException("Group not owned by active Tenant.");
            await db.Entry(group).ReloadAsync(ct);
            if (group.TenantId != tenantId)
                throw new UnauthorizedAccessException("Group reassigned outside active Tenant.");
            if (group.RowVersion is null || !group.RowVersion.SequenceEqual(expected))
                throw new TenantGroupLifecycleConflictException("Group changed; reload.");
            var previousName = group.Name;
            var previousActive = group.IsActive;
            if (!mutation(group))
            {
                await tx.CommitAsync(ct);
                return View(group);
            }
            // SQL RowVersion + EF concurrency CAS; no concurrent overwrite.
            await db.SaveChangesAsync(ct);
            await AuditAsync(db, tenantId, current.UserId!.Value, group,
                auditAction, previousName, previousActive, ct);
            await tx.CommitAsync(ct);
            return View(group);
        });
    }

    private static Task AuditAsync(
        IdentityDbContext db, Guid tenantId, Guid actor, Group group,
        string action, string? previousName, bool? previousActive, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null ||
            action is not ("create" or "update" or "state"))
            throw new InvalidOperationException("Tenant Group audit requires SQL transaction.");
        var id = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantGroupChangeAuditEntries]
                ([Id],[TenantId],[ActorUserId],[GroupId],[Action],
                 [PreviousName],[CurrentName],[PreviousActive],[CurrentActive])
            VALUES ({id},{tenantId},{actor},{group.Id},{action},
                    {previousName},{group.Name},{previousActive},{group.IsActive})
            """, ct);
    }
}
