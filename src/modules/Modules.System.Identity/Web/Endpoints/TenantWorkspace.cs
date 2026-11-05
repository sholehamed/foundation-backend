using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
    }

    private static async Task<IResult> Get(
        ITenantContext current,
        IdentityDbContext db,
        CancellationToken cancellationToken)
    {
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
        return Results.Ok(new {
            tenant, profile, mainBranch,
            subscriptionStatus = "pending_activation"
        });
    }
}
