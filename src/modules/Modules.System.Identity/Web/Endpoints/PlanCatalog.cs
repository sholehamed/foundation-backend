using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Only the live Master SystemAdministrator may author and publish Plans.
/// Tenant Owners NEVER define plan offerings, module ownership or prices.
/// </summary>
public sealed class PlanCatalog : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")
            .MapGet(List, "", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.LIST", "plan-catalog", "PLAN-CATALOG.READ", "Plan catalog"))
            .MapGet(EligibleModules, "commercial-modules", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.COMMERCIAL-MODULES", "plan-catalog", "PLAN-CATALOG.READ",
                "Reviewed commercial modules"))
            .MapPost(Create, "", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.CREATE", "plan-catalog", "PLAN-CATALOG.CREATE", "Create plan draft"))
            .MapPost(NewRevision, "{planId:guid}/drafts", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.NEW-REVISION", "plan-catalog", "PLAN-CATALOG.CREATE",
                "Create plan revision draft"))
            .MapPut(SaveDraft, "drafts/{versionId:guid}", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.SAVE-DRAFT", "plan-catalog", "PLAN-CATALOG.UPDATE", "Save plan draft"))
            .MapGet(ReadDraft, "drafts/{versionId:guid}", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.DRAFT", "plan-catalog", "PLAN-CATALOG.READ", "Read plan draft"))
            .MapGet(Preview, "drafts/{versionId:guid}/preview", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.PREVIEW", "plan-catalog", "PLAN-CATALOG.READ", "Review draft"))
            .MapPost(Publish, "drafts/{versionId:guid}/publish", configure: e => e.WithFeatureEndpoint(
                "SYSTEM.PLAN-CATALOG.PUBLISH", "plan-catalog", "PLAN-CATALOG.PUBLISH",
                "Commit reviewed publication"));
    }

    private static async Task<bool> IsLiveSystemAdministratorAsync(
        ITenantContext current, IdentityDbContext db, CancellationToken ct)
    {
        if (!current.IsSystemContext || !current.IsSuperAdmin ||
            current.UserId is not Guid actor)
            return false;
        if (!await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == actor && x.UserType == UserType.System && x.IsActive, ct))
            return false;
        var master = DefaultSeedData.MasterTenant.Id;
        return await db.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
            .AnyAsync(ur => ur.UserId == actor && ur.TenantId == master &&
                ur.Role.TenantId == master && ur.Role.NormalizedName == "SUPERADMIN" &&
                ur.Role.IsActive, ct);
    }

    private static async Task<IResult> List(
        ITenantContext actor, IdentityDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        return Results.Ok(await new SystemPlanCatalogAdminService(db).ListAsync(ct));
    }

    private static async Task<IResult> EligibleModules(
        ITenantContext actor, IdentityDbContext db, HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        return Results.Ok(await new SystemPlanCatalogAdminService(db).CommercialModulesAsync(ct));
    }

    private static async Task<IResult> Create(
        ITenantContext actor, IdentityDbContext db, HttpContext http,
        SystemPlanCatalogAdminService.NewPlan input, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        try
        {
            var result = await new SystemPlanCatalogAdminService(db).CreateAsync(input, ct);
            return result.Ok
                ? Results.Created($"/api/system/PlanCatalog/drafts/{result.Id}/preview", result)
                : Results.Conflict(result);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "catalog_changed_retry" });
        }
    }

    private static async Task<IResult> NewRevision(
        ITenantContext actor, IdentityDbContext db, HttpContext http,
        Guid planId, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        try
        {
            var result = await new SystemPlanCatalogAdminService(db).CreateRevisionAsync(planId, ct);
            return result.Ok ? Results.Ok(result) : Results.Conflict(result);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "catalog_changed_retry" });
        }
    }

    private static async Task<IResult> SaveDraft(
        ITenantContext actor, IdentityDbContext db, HttpContext http, Guid versionId,
        SystemPlanCatalogAdminService.DraftInput input, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        try
        {
            var result = await new SystemPlanCatalogAdminService(db)
                .ReplaceDraftAsync(versionId, input, ct);
            return result.Ok ? Results.Ok(result) : Results.Conflict(result);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "catalog_changed_retry" });
        }
    }

    private static async Task<IResult> ReadDraft(
        ITenantContext actor, IdentityDbContext db, HttpContext http,
        Guid versionId, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        var draft = await new SystemPlanCatalogAdminService(db).ReadDraftAsync(versionId, ct);
        return draft is null ? Results.NotFound() : Results.Ok(draft);
    }

    private static async Task<IResult> Preview(
        ITenantContext actor, IdentityDbContext db, HttpContext http,
        Guid versionId, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        return Results.Ok(await new SystemPlanCatalogAdminService(db)
            .PreviewAsync(versionId, ct));
    }

    public sealed record PublishRequest(string Fingerprint);

    private static async Task<IResult> Publish(
        ITenantContext actor, IdentityDbContext db, HttpContext http,
        Guid versionId, PublishRequest input, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!await IsLiveSystemAdministratorAsync(actor, db, ct)) return Results.Forbid();
        try
        {
            var result = await new SystemPlanCatalogAdminService(db)
                .PublishAsync(versionId, input.Fingerprint, ct);
            return result.Ok ? Results.Ok(result) : Results.Conflict(result);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "catalog_changed_retry" });
        }
    }
}
