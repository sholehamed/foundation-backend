using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;

namespace Modules.System.Identity.Web.Util;

public sealed record TenantCatalogChange(string Entity, string Key, string State, string? Detail = null);
public sealed record TenantCatalogPreview(
    string Fingerprint, bool CanApply, bool HasPending, IReadOnlyList<TenantCatalogChange> Changes);

/// <summary>
/// Code-owned, GLOBAL permission + UI definitions for the Tenant workspace.
/// Preview is strictly read-only. Apply is explicit (deployment flag + system
/// administrator endpoint), additive, serializable, and never creates grants,
/// menu entries, tenants or changes existing operator-controlled records.
/// </summary>
public sealed class TenantPermissionCatalogSync(IdentityDbContext db)
{
    public const string ModuleCode = "tenant-workspace";
    public const string ResourceCode = "tenant-administration";

    public async Task<TenantCatalogPreview> PreviewAsync(CancellationToken ct = default)
    {
        var modules = await db.Modules.AsNoTracking().ToListAsync(ct);
        var resources = await db.Resources.AsNoTracking().ToListAsync(ct);
        var permissions = await db.Permissions.AsNoTracking().ToListAsync(ct);
        var ui = await db.UiResources.AsNoTracking().ToListAsync(ct);
        var links = await db.PermissionUiResources.AsNoTracking().ToListAsync(ct);

        var changes = new List<TenantCatalogChange>();
        var fingerprintFacts = new List<string>();
        static bool Same(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        void Add(string entity, string key, string state, string? detail = null) =>
            changes.Add(new(entity, key, state, detail));

        var ms = modules.Where(x => Same(x.Code, ModuleCode)).ToList();
        var module = ms.Count == 1 ? ms[0] : null;
        fingerprintFacts.AddRange(ms.OrderBy(x => x.Id).Select(x =>
            $"module:{x.Id:N}:{x.Code}:{x.Title}:{x.IsActive}"));
        Add("Module", ModuleCode, ms.Count switch
        {
            0 => "Create",
            1 when module!.IsActive => "Present",
            _ => "Conflict"
        }, ms.Count > 1 ? "Duplicate module code" :
            module is { IsActive: false } ? "Inactive module: operator review required" : null);

        // A resource with this code under a different module is unowned drift,
        // not permission to overwrite another module's Resource.
        var rs = resources.Where(x => Same(x.Code, ResourceCode)).ToList();
        fingerprintFacts.AddRange(rs.OrderBy(x => x.Id).Select(x =>
            $"resource:{x.Id:N}:{x.ModuleId:N}:{x.Code}:{x.Title}:{x.IsActive}"));
        var resource = rs.Count == 1 && module is not null &&
            rs[0].ModuleId == module.Id ? rs[0] : null;
        Add("Resource", ResourceCode, rs.Count switch
        {
            0 => "Create",
            1 when resource is { IsActive: true } => "Present",
            1 when module is null => "Conflict",
            _ => "Conflict"
        }, rs.Count > 0 && resource is null
            ? "Resource code already owned by a different or unresolved module" :
            resource is { IsActive: false } ? "Inactive resource" : null);

        foreach (var definition in TenantWorkspacePermissionCatalog.Definitions)
        {
            var ps = permissions.Where(x => Same(x.Code, definition.Key)).ToList();
            fingerprintFacts.AddRange(ps.OrderBy(x => x.Id).Select(x =>
                $"permission:{x.Id:N}:{x.Code}:{x.ResourceId}:{x.Name}:{x.IsActive}"));
            var p = ps.Count == 1 ? ps[0] : null;
            string pState;
            if (ps.Count == 0) pState = "Create";
            else if (p is null || resource is null ||
                     p.ResourceId != resource.Id || !p.IsActive)
                pState = "Conflict";
            else pState = "Present";
            Add("Permission", definition.Key, pState,
                ps.Count > 1 ? "Duplicate global Permission.Code" :
                pState == "Conflict" ? "Inactive or foreign-owned permission; never adopt silently" : null);

            var us = ui.Where(x => Same(x.Key, definition.UiKey)).ToList();
            fingerprintFacts.AddRange(us.OrderBy(x => x.Id).Select(x =>
                $"ui:{x.Id:N}:{x.ResourceId:N}:{x.Key}:{x.Title}:{x.Type}:{x.Description}:{x.IsActive}"));
            var u = us.Count == 1 ? us[0] : null;
            var uiType = definition.Kind == "PAGE" ? UiResourceType.PAGE : UiResourceType.ACTION;
            string uiState;
            if (us.Count == 0) uiState = "Create";
            else if (u is null || resource is null || u.ResourceId != resource.Id ||
                     u.Type != uiType || !u.IsActive ||
                     !string.Equals(u.Description, definition.Kind == "PAGE" ? definition.Route : null,
                         StringComparison.Ordinal))
                uiState = "Conflict";
            else uiState = "Present";
            Add("UiResource", definition.UiKey, uiState,
                uiState == "Conflict" ? "Inactive or incompatible UI record; manual review required" : null);

            var matching = (p is not null && u is not null)
                ? links.Count(x => x.PermissionId == p.Id && x.UiResourceId == u.Id)
                : 0;
            if (p is not null && u is not null)
                fingerprintFacts.AddRange(links.Where(x =>
                    x.PermissionId == p.Id && x.UiResourceId == u.Id)
                    .OrderBy(x => x.Id).Select(x =>
                        $"link:{x.Id:N}:{x.PermissionId:N}:{x.UiResourceId:N}"));
            Add("PermissionUiResource", definition.Key + " -> " + definition.UiKey,
                matching switch
                {
                    0 => "Create",
                    1 => "Present",
                    _ => "Conflict"
                }, matching > 1 ? "Duplicate permission/UI link" : null);
        }

        // Fingerprint binds an approved preview to the exact examined states.
        // Serializable SQL transaction and uniqueness constraints remain the
        // final defense against changing DB contents and concurrent Applies.
        var seed = string.Join("\n", changes.Select(x =>
            x.Entity + "|" + x.Key + "|" + x.State + "|" + x.Detail)) +
                   "\n" + string.Join("\n", fingerprintFacts);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        return new TenantCatalogPreview(hash,
            !changes.Any(x => x.State == "Conflict"),
            changes.Any(x => x.State == "Create"), changes);
    }

    public async Task<TenantCatalogPreview> ApplyAsync(
        string expectedFingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint) ||
            expectedFingerprint.Length != 64)
            throw new ArgumentException("A reviewed 64-character preview fingerprint is required.");

        // Relational SQL transaction is mandatory for catalog writes.
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("Tenant catalog apply requires a relational database.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var plan = await PreviewAsync(ct);
            if (!plan.CanApply || !string.Equals(plan.Fingerprint,
                    expectedFingerprint, StringComparison.Ordinal))
                return plan; // rolls back uncommitted transaction on disposal

            if (!plan.HasPending)
            {
                await transaction.CommitAsync(ct);
                return plan;
            }

            var module = await db.Modules.SingleOrDefaultAsync(m => m.Code == ModuleCode, ct);
            if (module is null)
            {
                module = new Module(Guid.NewGuid(), ModuleCode, "Tenant Workspace",
                    "Tenant-owned navigation and actions", 0, true) { RowVersion = [1] };
                db.Modules.Add(module);
            }

            var resource = await db.Resources.SingleOrDefaultAsync(
                r => r.Code == ResourceCode && r.ModuleId == module.Id, ct);
            if (resource is null)
            {
                resource = new Resource(Guid.NewGuid(), module.Id, ResourceCode,
                    "Tenant Administration", null, 0, true) { RowVersion = [1] };
                db.Resources.Add(resource);
            }

            foreach (var definition in TenantWorkspacePermissionCatalog.Definitions)
            {
                var p = await db.Permissions.SingleOrDefaultAsync(
                    x => x.Code == definition.Key, ct);
                if (p is null)
                {
                    p = new Permission(Guid.NewGuid(), definition.Key, resource.Id,
                        definition.Name, "Code-owned Tenant permission", true)
                    { RowVersion = [1] };
                    db.Permissions.Add(p);
                }

                var u = await db.UiResources.SingleOrDefaultAsync(
                    x => x.Key == definition.UiKey, ct);
                if (u is null)
                {
                    u = new UiResource(Guid.NewGuid(), resource.Id, definition.UiKey,
                        definition.Name,
                        definition.Kind == "PAGE" ? definition.Route : null,
                        definition.Kind == "PAGE" ? UiResourceType.PAGE : UiResourceType.ACTION)
                        { RowVersion = [1], IsActive = true };
                    db.UiResources.Add(u);
                }

                var exists = await db.PermissionUiResources.AsNoTracking().AnyAsync(x =>
                    x.PermissionId == p.Id && x.UiResourceId == u.Id, ct);
                if (!exists)
                    db.PermissionUiResources.Add(
                        new PermissionUiResource(p.Id, u.Id) { RowVersion = [1] });
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return plan;
        });
    }
}
