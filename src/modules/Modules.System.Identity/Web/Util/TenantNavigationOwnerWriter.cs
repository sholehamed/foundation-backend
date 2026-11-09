using System.Data;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Endpoints;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

public sealed class TenantNavigationInputException : Exception { }
public sealed class TenantNavigationVersionConflictException : Exception { }

/// <summary>
/// FR-14C-2B. Dedicated SQL-only, Owner-controlled catalog authoring.
/// Never uses global Menus, general CRUD, caller-supplied TenantId, implicit
/// BFF permissions, or EF's broad SaveChanges for tenant-owned navigation.
/// The two owner permissions are registered separately: READ and MANAGE.
/// </summary>
public static class TenantNavigationOwnerWriter
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateCategoryRequest(string? Title, short SortOrder);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record UpdateCategoryRequest(string? Title, short SortOrder,
        [property: JsonRequired] bool IsActive, string? ExpectedVersion);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateItemRequest(Guid CategoryId, Guid? ParentId,
        string? Title, string? Icon, string? Route, short SortOrder);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record UpdateItemRequest(Guid? ParentId,
        string? Title, string? Icon, string? Route, short SortOrder,
        [property: JsonRequired] bool IsActive, string? ExpectedVersion);

    public sealed record CategoryView(Guid Id, string Title, short SortOrder,
        bool IsActive, string Version);
    public sealed record ItemView(Guid Id, Guid CategoryId, Guid? ParentId,
        string Title, string? Icon, string? Route, short SortOrder,
        bool IsActive, string Version);
    public sealed record CatalogView(IReadOnlyList<CategoryView> Categories,
        IReadOnlyList<ItemView> Items, bool CanManage);

    public sealed class CategoryRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public short SortOrder { get; set; }
        public bool IsActive { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }

    public sealed class ItemRow
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public Guid? ParentId { get; set; }
        public string Title { get; set; } = "";
        public string? Icon { get; set; }
        public string? Route { get; set; }
        public short SortOrder { get; set; }
        public bool IsActive { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }

    private static CategoryView ToView(CategoryRow x) =>
        new(x.Id, x.Title, x.SortOrder, x.IsActive,
            Convert.ToBase64String(x.RowVersion));

    private static ItemView ToView(ItemRow x) =>
        new(x.Id, x.CategoryId, x.ParentId, x.Title, x.Icon, x.Route,
            x.SortOrder, x.IsActive, Convert.ToBase64String(x.RowVersion));

    private static string Title(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length is < 2 or > 120 || text.Any(char.IsControl))
            throw new TenantNavigationInputException();
        return text;
    }

    private static string? Icon(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > 80 ||
            !text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new TenantNavigationInputException();
        return text;
    }

    private static string? Route(string? value)
    {
        if (value is null) return null;
        if (value.Length > 240 ||
            !TenantWorkspaceNavigationService.RoutePermissions.ContainsKey(value))
            throw new TenantNavigationInputException();
        return value;
    }

    private static void Order(short sortOrder)
    {
        if (sortOrder is < 0 or > 10000)
            throw new TenantNavigationInputException();
    }

    private static byte[] Version(string? encoded)
    {
        if (encoded is not { Length: 12 })
            throw new TenantNavigationInputException();
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length != 8 ||
                !string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal))
                throw new TenantNavigationInputException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new TenantNavigationInputException();
        }
    }

    private static async Task<Guid> AuthorizeAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        bool canWrite, bool enabled, CancellationToken ct)
    {
        if (!enabled || !db.Database.IsSqlServer() ||
            principal.Identity?.IsAuthenticated != true ||
            principal.FindFirstValue("account_type") != "tenant" ||
            principal.HasClaim(c => c.Type == "system") ||
            !Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject),
                out var actor) || actor == Guid.Empty ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var claimTenant) ||
            current.UserId != actor ||
            current.IsSystemContext ||
            current.ActiveTenantId is not Guid tenantId || tenantId == Guid.Empty ||
            current.HomeTenantId != tenantId || claimTenant != tenantId ||
            !current.ReadableTenantIds.Contains(tenantId) ||
            (canWrite && !current.WritableTenantIds.Contains(tenantId)))
            throw new UnauthorizedAccessException("Tenant navigation is not authorized.");

        var tenantSlug = principal.FindFirstValue("tenant");
        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(tenantSlug) ||
            !await db.Tenants.AsNoTracking().AnyAsync(x =>
                x.Id == tenantId && x.IsActive && x.Slug == tenantSlug, ct) ||
            !await db.Users.AsNoTracking().AnyAsync(x =>
                x.Id == actor && x.TenantId == tenantId &&
                x.UserType == UserType.Tenant && x.IsActive &&
                (!x.IsLockedOut || x.LockoutEndUtc <= now), ct))
            throw new UnauthorizedAccessException();

        var key = canWrite
            ? TenantWorkspacePermissionCatalog.NavigationManage
            : TenantWorkspacePermissionCatalog.NavigationRead;
        // New permissions must be explicitly registered and active. Existing
        // legacy Owner fallback cannot authorize a missing catalog definition.
        if (await db.Permissions.AsNoTracking().CountAsync(
                x => x.Code == key && x.IsActive, ct) != 1 ||
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, key, ct))
            throw new UnauthorizedAccessException();

        // First rollout is *Owner-only*, not delegated menu editing.
        // Explicit MANAGE role grant alone cannot mutate a whole Tenant menu,
        // as that could suppress pages a delegate is not permitted to access.
        if (canWrite && !await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(
                current, db, ct))
            throw new UnauthorizedAccessException();
        return tenantId;
    }

    private static Task<List<CategoryRow>> CategoriesAsync(
        IdentityDbContext db, Guid tenant, CancellationToken ct)
        => db.Database.SqlQueryRaw<CategoryRow>(
            """
            SELECT [Id],[Title],[SortOrder],[IsActive],[RowVersion]
            FROM [dbo].[TenantNavigationCategories]
            WHERE [TenantId]={0}
            """, tenant).ToListAsync(ct);

    private static Task<List<ItemRow>> ItemsAsync(
        IdentityDbContext db, Guid tenant, CancellationToken ct)
        => db.Database.SqlQueryRaw<ItemRow>(
            """
            SELECT [Id],[CategoryId],[ParentId],[Title],[Icon],[Route],
                   [SortOrder],[IsActive],[RowVersion]
            FROM [dbo].[TenantNavigationItems]
            WHERE [TenantId]={0}
            """, tenant).ToListAsync(ct);

    private static Task AuditAsync(IdentityDbContext db,
        Guid tenantId, Guid actor, Guid target, string entity, string action,
        byte[]? before, byte[] after, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[TenantNavigationAuditEntries]
                ([Id],[TenantId],[ActorUserId],[EntityId],[EntityType],[Action],
                 [PreviousVersion],[CurrentVersion])
            VALUES ({Guid.NewGuid()},{tenantId},{actor},{target},{entity},{action},
                    {before},{after})
            """, ct);

    private static async Task EnsureRouteAsync(
        ITenantContext current, IdentityDbContext db, string? route, CancellationToken ct)
    {
        if (route is not null &&
            TenantWorkspaceNavigationService.RoutePermissions[route] is string permission &&
            !await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db, permission, ct))
            throw new UnauthorizedAccessException("The navigation route is not accessible.");
    }

    public static async Task<CatalogView> ReadAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        bool enabled, CancellationToken ct = default)
    {
        var tenant = await AuthorizeAsync(current, db, principal,
            canWrite: false, enabled, ct);
        var categories = await CategoriesAsync(db, tenant, ct);
        var items = await ItemsAsync(db, tenant, ct);
        var editable = current.WritableTenantIds.Contains(tenant) &&
            await db.Permissions.AsNoTracking().CountAsync(x =>
                x.Code == TenantWorkspacePermissionCatalog.NavigationManage &&
                x.IsActive, ct) == 1 &&
            await TenantWorkspacePermissionEvaluator.AllowsAsync(current, db,
                TenantWorkspacePermissionCatalog.NavigationManage, ct) &&
            await TenantWorkspacePermissionEvaluator.IsLocalOwnerAsync(current, db, ct);
        return new CatalogView(
            categories.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(ToView).ToList(),
            items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(ToView).ToList(),
            editable);
    }

    public static async Task<CategoryView> CreateCategoryAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        CreateCategoryRequest? input, bool enabled, CancellationToken ct = default)
    {
        if (input is null) throw new TenantNavigationInputException();
        var title = Title(input.Title);
        Order(input.SortOrder);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var tenant = await AuthorizeAsync(current, db, principal, true, enabled, ct);
            var actor = current.UserId!.Value;
            if (await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantNavigationCategories] WHERE [TenantId]={0}",
                tenant).SingleAsync(ct) >= 128)
                throw new TenantNavigationVersionConflictException();

            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationCategories]
                    ([TenantId],[Id],[Title],[SortOrder])
                VALUES ({tenant},{id},{title},{input.SortOrder})
                """, ct);
            var created = (await CategoriesAsync(db, tenant, ct))
                .Single(x => x.Id == id);
            await AuditAsync(db, tenant, actor, id,
                "category", "category_created", null, created.RowVersion, ct);
            await tx.CommitAsync(ct);
            return ToView(created);
        });
    }

    public static async Task<CategoryView> UpdateCategoryAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        Guid id, UpdateCategoryRequest? input, bool enabled,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty || input is null)
            throw new TenantNavigationInputException();
        var title = Title(input.Title);
        Order(input.SortOrder);
        var expected = Version(input.ExpectedVersion);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var tenant = await AuthorizeAsync(current, db, principal, true, enabled, ct);
            var actor = current.UserId!.Value;
            var existing = (await CategoriesAsync(db, tenant, ct))
                .SingleOrDefault(x => x.Id == id);
            if (existing is null) throw new UnauthorizedAccessException();
            if (!existing.RowVersion.SequenceEqual(expected))
                throw new TenantNavigationVersionConflictException();
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantNavigationCategories]
                SET [Title]={title}, [SortOrder]={input.SortOrder},
                    [IsActive]={input.IsActive}
                WHERE [TenantId]={tenant} AND [Id]={id} AND [RowVersion]={expected}
                """, ct);
            if (updated != 1) throw new TenantNavigationVersionConflictException();
            var next = (await CategoriesAsync(db, tenant, ct)).Single(x => x.Id == id);
            await AuditAsync(db, tenant, actor, id, "category",
                "category_updated", expected, next.RowVersion, ct);
            await tx.CommitAsync(ct);
            return ToView(next);
        });
    }

    public static async Task<ItemView> CreateItemAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        CreateItemRequest? input, bool enabled, CancellationToken ct = default)
    {
        if (input is null || input.CategoryId == Guid.Empty)
            throw new TenantNavigationInputException();
        var title = Title(input.Title);
        var icon = Icon(input.Icon);
        var route = Route(input.Route);
        Order(input.SortOrder);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var tenant = await AuthorizeAsync(current, db, principal, true, enabled, ct);
            var actor = current.UserId!.Value;
            await EnsureRouteAsync(current, db, route, ct);
            var categories = await CategoriesAsync(db, tenant, ct);
            if (!categories.Any(c => c.Id == input.CategoryId && c.IsActive))
                throw new UnauthorizedAccessException();
            var items = await ItemsAsync(db, tenant, ct);
            if (items.Count >= 1024)
                throw new TenantNavigationVersionConflictException();
            ValidateParent(items, input.CategoryId, Guid.Empty, input.ParentId);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[TenantNavigationItems]
                    ([TenantId],[Id],[CategoryId],[ParentId],[Title],
                     [Icon],[Route],[SortOrder])
                VALUES ({tenant},{id},{input.CategoryId},{input.ParentId},
                    {title},{icon},{route},{input.SortOrder})
                """, ct);
            var created = (await ItemsAsync(db, tenant, ct)).Single(x => x.Id == id);
            await AuditAsync(db, tenant, actor, id,
                "item", "item_created", null, created.RowVersion, ct);
            await tx.CommitAsync(ct);
            return ToView(created);
        });
    }

    public static async Task<ItemView> UpdateItemAsync(
        ITenantContext current, IdentityDbContext db, ClaimsPrincipal principal,
        Guid id, UpdateItemRequest? input, bool enabled,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty || input is null)
            throw new TenantNavigationInputException();
        var title = Title(input.Title);
        var icon = Icon(input.Icon);
        var route = Route(input.Route);
        Order(input.SortOrder);
        var expected = Version(input.ExpectedVersion);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);
            var tenant = await AuthorizeAsync(current, db, principal, true, enabled, ct);
            var actor = current.UserId!.Value;
            var items = await ItemsAsync(db, tenant, ct);
            var existing = items.SingleOrDefault(x => x.Id == id);
            if (existing is null)
                throw new UnauthorizedAccessException();
            if (!existing.RowVersion.SequenceEqual(expected))
                throw new TenantNavigationVersionConflictException();

            await EnsureRouteAsync(current, db, existing.Route, ct);
            await EnsureRouteAsync(current, db, route, ct);
            if (!await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM [dbo].[TenantNavigationCategories] " +
                    "WHERE [TenantId]={0} AND [Id]={1} AND [IsActive]=1",
                    tenant, existing.CategoryId).SingleAsync(ct) == 0)
                throw new UnauthorizedAccessException();
            ValidateParent(items, existing.CategoryId, id, input.ParentId);

            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [dbo].[TenantNavigationItems]
                SET [ParentId]={input.ParentId}, [Title]={title},
                    [Icon]={icon}, [Route]={route},
                    [SortOrder]={input.SortOrder}, [IsActive]={input.IsActive}
                WHERE [TenantId]={tenant} AND [Id]={id} AND
                    [CategoryId]={existing.CategoryId} AND [RowVersion]={expected}
                """, ct);
            if (affected != 1) throw new TenantNavigationVersionConflictException();
            var next = (await ItemsAsync(db, tenant, ct)).Single(x => x.Id == id);
            await AuditAsync(db, tenant, actor, id, "item",
                "item_updated", expected, next.RowVersion, ct);
            await tx.CommitAsync(ct);
            return ToView(next);
        });
    }

    private static void ValidateParent(
        IReadOnlyList<ItemRow> items, Guid categoryId, Guid selfId, Guid? parent)
    {
        var seen = new HashSet<Guid>();
        var depth = 0;
        while (parent is Guid ancestor)
        {
            if (ancestor == selfId || !seen.Add(ancestor) || ++depth >= 32)
                throw new TenantNavigationInputException();
            var row = items.SingleOrDefault(x => x.Id == ancestor &&
                x.CategoryId == categoryId && x.IsActive);
            if (row is null) throw new UnauthorizedAccessException();
            parent = row.ParentId;
        }
    }
}
