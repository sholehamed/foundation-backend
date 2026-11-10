using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// FR-17B-2A public, *read-only* DB-driven plan cards.
/// This is catalog display, not checkout, an active subscription or authorization.
/// An unavailable/unregistered commercial module must never be advertised as active.
/// </summary>
public static class PublicPlans
{
    public sealed record PublicPrice(string BillingCycle, string Currency, decimal Amount);
    public sealed record PublicFeature(string Kind, string Title, string? Description, bool IsHighlighted);
    public sealed record PublicPlanCard(
        string Code, Guid VersionId, string Title, string? Subtitle,
        string? Badge, bool IsRecommended, int DisplayOrder,
        IReadOnlyList<PublicPrice> Prices, IReadOnlyList<PublicFeature> Features);

    public static void Map(WebApplication app, string backendPrefix)
    {
        app.MapGet($"/{backendPrefix.Trim('/')}/public/plans", List)
            .AllowAnonymous()
            .RequireRateLimiting("public-plans")
            .WithTags("Public Plans");
    }

    public static async Task<IResult> List(
        IdentityDbContext db, ILoggerFactory loggerFactory,
        HttpContext http, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var cards = await ReadCardsAsync(
            db, ModuleResourceContracts.Current(), [],
            DateTime.UtcNow, loggerFactory.CreateLogger("PublicPlans"), ct);
        return Results.Ok(cards);
    }

    /// <summary>
    /// Separately testable projection. Source contracts are caller-supplied
    /// code-owned definitions, not database-controlled privileges.
    /// </summary>
    public static async Task<IReadOnlyList<PublicPlanCard>> ReadCardsAsync(
        IdentityDbContext db,
        IReadOnlyCollection<ResourceContract> resourceContracts,
        IReadOnlyCollection<CapabilityContract> capabilityContracts,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct = default)
    {
        var selected = (await db.CatalogPlanVersions.AsNoTracking()
            .Where(x => x.Status == "Published" &&
                        x.PublishedAtUtc != null &&
                        x.PublishedAtUtc <= nowUtc)
            .OrderByDescending(x => x.VersionNumber)
            .ToArrayAsync(ct))
            .DistinctBy(x => x.PlanId)
            .ToArray();

        var reader = new PublishedPlanCatalogReader(db);
        var cards = new List<PublicPlanCard>();
        foreach (var version in selected)
        {
            try
            {
                var plan = await reader.ReadAsync(
                    version.Id, resourceContracts, capabilityContracts, nowUtc, ct);
                // No empty, non-saleable or unregistered products. An active
                // display page is not proof of a purchasable entitlement.
                if (plan.ModuleCodes.Count == 0 || plan.Prices.Count == 0)
                    continue;

                cards.Add(new PublicPlanCard(
                    plan.PlanCode, plan.PlanVersionId, plan.DisplayTitle,
                    plan.DisplaySubtitle, plan.BadgeText, plan.IsRecommended,
                    plan.DisplayOrder,
                    plan.Prices.Select(x => new PublicPrice(
                        x.BillingCycle, x.Currency, x.Amount)).ToArray(),
                    plan.DisplayItems.Select(x => new PublicFeature(
                        x.Kind, x.Title, x.Description, x.IsHighlighted)).ToArray()));
            }
            catch (InvalidOperationException)
            {
                // The corrupt/unreviewed version is NOT exposed. Never fall back
                // to a hardcoded package or leak a plan's internal catalog.
                logger.LogWarning("Public catalog skipped invalid plan version {PlanVersionId}",
                    version.Id);
            }
        }
        return cards.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code, StringComparer.Ordinal)
            .Take(50).ToArray();
    }
}
