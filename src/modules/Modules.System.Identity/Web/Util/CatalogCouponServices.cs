using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Server-generated 96-bit codes, SHA256 at rest. A Pending quote does not
/// reserve or consume a use; SQL SERIALIZABLE + coupon UPDLOCK/HOLDLOCK
/// serializes definitive settlement/redemption across all Tenants.
/// </summary>
public static class CatalogCouponRules
{
    public static string Fingerprint(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "Foundation.FR17E.v1:" + code.Trim().ToUpperInvariant())));

    public static string GenerateCode() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    public static decimal Discount(CatalogDiscountCoupon coupon, decimal gross) =>
        coupon.Kind == "Percentage"
            ? Math.Min(gross, decimal.Round(gross * coupon.Value / 100m,
                0, MidpointRounding.AwayFromZero))
            : Math.Min(gross, coupon.Value);

    /// <summary>
    /// Caller holds a SERIALIZABLE transaction for settlement.
    /// When forSettlement=true acquire a row update lock before counting
    /// existing global/per-tenant settled redemptions.
    /// </summary>
    public static async Task<(CatalogDiscountCoupon? Coupon, string Error)>
        CheckAsync(IdentityDbContext db, string codeOrId, Guid tenantId,
            Guid planVersionId, decimal gross, DateTime nowUtc, bool forSettlement,
            CancellationToken ct, bool isId = false)
    {
        if (!db.Database.IsSqlServer() || tenantId == Guid.Empty || gross <= 0)
            return (null, "coupon_invalid");
        CatalogDiscountCoupon? coupon;
        if (isId)
        {
            if (!Guid.TryParse(codeOrId, out var couponId))
                return (null, "coupon_invalid");
            coupon = forSettlement
                ? await db.CatalogDiscountCoupons.FromSqlInterpolated(
                    $"SELECT * FROM dbo.CatalogDiscountCoupons WITH (UPDLOCK,HOLDLOCK) WHERE Id = {couponId}")
                    .SingleOrDefaultAsync(ct)
                : await db.CatalogDiscountCoupons.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == couponId, ct);
        }
        else
        {
            if (codeOrId is null || codeOrId.Trim().Length != 24 ||
                codeOrId.Any(c => !Uri.IsHexDigit(c)))
                return (null, "coupon_invalid");
            var fingerprint = Fingerprint(codeOrId);
            coupon = forSettlement
                ? await db.CatalogDiscountCoupons.FromSqlInterpolated(
                    $"SELECT * FROM dbo.CatalogDiscountCoupons WITH (UPDLOCK,HOLDLOCK) WHERE CodeHash = {fingerprint}")
                    .SingleOrDefaultAsync(ct)
                : await db.CatalogDiscountCoupons.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CodeHash == fingerprint, ct);
        }

        if (coupon is null || !coupon.IsActive || nowUtc < coupon.StartsAtUtc ||
            nowUtc >= coupon.EndsAtUtc ||
            (coupon.PlanVersionId is Guid pinned && pinned != planVersionId) ||
            coupon.Currency != "IRR" || coupon.MaxUses <= 0 ||
            coupon.MaxUsesPerTenant <= 0 || coupon.MaxUsesPerTenant > coupon.MaxUses ||
            coupon.Value <= 0 || coupon.Kind is not ("Percentage" or "FixedIRR") ||
            (coupon.Kind == "Percentage" && coupon.Value > 100))
            return (null, "coupon_invalid_or_expired");

        // Ignore the current tenant filter *only* for a global cap check.
        // The caller serializes all coupon settlements by locking the row.
        var redeemed = db.CatalogCouponRedemptions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => x.CouponId == coupon.Id);
        if (await redeemed.CountAsync(ct) >= coupon.MaxUses ||
            await redeemed.CountAsync(x => x.TenantId == tenantId, ct) >= coupon.MaxUsesPerTenant)
            return (null, "coupon_usage_exhausted");

        if (Discount(coupon, gross) <= 0)
            return (null, "coupon_zero_discount");
        return (coupon, "");
    }
}

public sealed class SystemCouponCatalogService(IdentityDbContext db)
{
    public sealed record IssueCoupon(
        string Title, string Kind, decimal Value,
        Guid? PlanVersionId, DateTime StartsAtUtc, DateTime EndsAtUtc,
        int MaxUses, int MaxUsesPerTenant);
    public sealed record CouponView(
        Guid Id, string Title, string Kind, decimal Value,
        Guid? PlanVersionId, DateTime StartsAtUtc, DateTime EndsAtUtc,
        int MaxUses, int MaxUsesPerTenant, int SettledUses, bool IsActive);
    public sealed record IssuedCoupon(bool Ok, string Code, CouponView? Coupon = null,
        string? SecretCode = null);

    public async Task<IReadOnlyList<CouponView>> ListAsync(CancellationToken ct)
    {
        var definitions = await db.CatalogDiscountCoupons.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Take(200).ToArrayAsync(ct);
        var ids = definitions.Select(x => x.Id).ToArray();
        var usages = await db.CatalogCouponRedemptions.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => ids.Contains(x.CouponId))
            .GroupBy(x => x.CouponId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToArrayAsync(ct);
        return definitions.Select(x => new CouponView(x.Id, x.Title, x.Kind,
            x.Value, x.PlanVersionId, x.StartsAtUtc, x.EndsAtUtc,
            x.MaxUses, x.MaxUsesPerTenant,
            usages.FirstOrDefault(y => y.Id == x.Id)?.Count ?? 0,
            x.IsActive)).ToArray();
    }

    public async Task<IssuedCoupon> IssueAsync(IssueCoupon request, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer() || request is null ||
            string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Length > 120 ||
            request.Kind is not ("Percentage" or "FixedIRR") ||
            request.Value <= 0 ||
            (request.Kind == "Percentage" && request.Value > 100) ||
            request.StartsAtUtc >= request.EndsAtUtc ||
            request.MaxUses is < 1 or > 1_000_000 ||
            request.MaxUsesPerTenant < 1 ||
            request.MaxUsesPerTenant > request.MaxUses)
            return new(false, "invalid_coupon_fields");

        if (request.PlanVersionId is Guid version &&
            !await db.CatalogPlanVersions.AsNoTracking()
                .AnyAsync(x => x.Id == version && x.Status == "Published", ct))
            return new(false, "invalid_pinned_plan_version");

        var code = CatalogCouponRules.GenerateCode();
        var coupon = new CatalogDiscountCoupon
        {
            Title = request.Title.Trim(), CodeHash = CatalogCouponRules.Fingerprint(code),
            Kind = request.Kind, Value = request.Value, Currency = "IRR",
            PlanVersionId = request.PlanVersionId, StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc, MaxUses = request.MaxUses,
            MaxUsesPerTenant = request.MaxUsesPerTenant, IsActive = true
        };
        db.CatalogDiscountCoupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return new(true, "coupon_issued",
            new CouponView(coupon.Id, coupon.Title, coupon.Kind, coupon.Value,
                coupon.PlanVersionId, coupon.StartsAtUtc, coupon.EndsAtUtc,
                coupon.MaxUses, coupon.MaxUsesPerTenant, 0, true),
            code);
    }

    public async Task<bool> DisableAsync(Guid id, CancellationToken ct)
    {
        var row = await db.CatalogDiscountCoupons
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.IsActive = false;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
