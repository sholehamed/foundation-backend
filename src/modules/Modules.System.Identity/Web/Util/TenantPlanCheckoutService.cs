using System.Data;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// FR-17C-2A. Creates server-priced pending orders and supports a
/// System-superadmin's explicit external-receipt attestation.
/// No gateway callback, coupon redemption, or direct RBAC grants.
/// </summary>
public sealed class TenantPlanCheckoutService(
    IdentityDbContext db,
    IReadOnlyCollection<ResourceContract>? resources = null,
    IReadOnlyCollection<CapabilityContract>? capabilities = null)
{
    private readonly IReadOnlyCollection<ResourceContract> resources =
        resources ?? ModuleResourceContracts.Current();
    private readonly IReadOnlyCollection<CapabilityContract> capabilities =
        capabilities ?? [];

    public sealed record CreateOrder(Guid PlanVersionId, string BillingCycle, string IdempotencyKey, string? CouponCode = null);
    public sealed record OrderView(Guid Id, Guid TenantId, Guid PlanVersionId,
        string Status, string BillingCycle, string Currency,
        decimal GrossAmount, decimal DiscountAmount, decimal NetAmount,
        DateTime QuotedAtUtc, DateTime? SettledAtUtc,
        string? SettlementKind, string? SettlementReference);
    public sealed record OrderResult(bool Ok, string Code, OrderView? Order = null,
        Guid? SubscriptionId = null);
    public sealed record ManualAttestation(string ReceiptReference, bool PaymentVerified);

    private static OrderView View(TenantPlanOrder x) => new(
        x.Id, x.TenantId, x.PlanVersionId, x.Status,
        x.BillingCycle, x.Currency, x.GrossAmount, x.DiscountAmount,
        x.NetAmount, x.QuotedAtUtc, x.SettledAtUtc,
        x.SettlementKind, x.SettlementReference);

    /// <summary>
    /// The caller must have been checked as a live Tenant Owner at HTTP
    /// boundary. The Tenant identifier comes from session, not request.
    /// </summary>
    public async Task<OrderResult> CreatePendingAsync(
        Guid tenantId, CreateOrder request, DateTime nowUtc, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || request is null ||
            request.PlanVersionId == Guid.Empty ||
            (request.CouponCode is not null &&
             (request.CouponCode.Trim().Length != 24 ||
              request.CouponCode.Any(c => !Uri.IsHexDigit(c)))) ||
            request.BillingCycle is not ("monthly" or "yearly") ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length is > 100 or < 16 ||
            request.IdempotencyKey.Any(x => !char.IsAsciiLetterOrDigit(x) &&
                                             x is not ('-' or '_')))
            return new(false, "invalid_request");
        if (!db.Database.IsSqlServer()) return new(false, "sql_server_required");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await db.Tenants.AsNoTracking()
                .AnyAsync(x => x.Id == tenantId && x.IsActive, ct))
                return new OrderResult(false, "tenant_inactive");

            var existing = await db.TenantPlanOrders
                .SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                    x.IdempotencyKey == request.IdempotencyKey, ct);
            if (existing is not null)
            {
                if (existing.PlanVersionId != request.PlanVersionId ||
                    existing.BillingCycle != request.BillingCycle ||
                    !await MatchesCouponAsync(existing.CouponId, request.CouponCode, ct))
                    return new OrderResult(false, "idempotency_conflict");
                return new OrderResult(true, "existing_order", View(existing));
            }
            // A lost HTTP response or a refreshed browser must not create
            // duplicate unpaid quotes for the same Tenant.
            var pending = await db.TenantPlanOrders.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Status == "Pending")
                .OrderByDescending(x => x.QuotedAtUtc).FirstOrDefaultAsync(ct);
            if (pending is not null)
                return pending.PlanVersionId == request.PlanVersionId &&
                       pending.BillingCycle == request.BillingCycle &&
                       await MatchesCouponAsync(pending.CouponId, request.CouponCode, ct)
                    ? new OrderResult(true, "existing_pending", View(pending))
                    : new OrderResult(false, "pending_order_exists");

            if (await db.TenantSubscriptions.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.Status == "Active" &&
                x.EndsAtUtc > nowUtc, ct))
                return new OrderResult(false, "active_subscription_exists");

            ResolvedCatalogPlan plan;
            try
            {
                plan = await new PublishedPlanCatalogReader(db).ReadAsync(
                    request.PlanVersionId, resources, capabilities, nowUtc, ct);
            }
            catch (InvalidOperationException)
            {
                return new OrderResult(false, "plan_not_saleable");
            }

            // No code-owned business implementation => no checkout.
            if (plan.ModuleCodes.Count == 0)
                return new OrderResult(false, "no_commercial_modules");

            var price = plan.Prices.SingleOrDefault(x =>
                x.BillingCycle == request.BillingCycle && x.Currency == "IRR");
            if (price is null || price.Amount < 0)
                return new OrderResult(false, "offer_unavailable");

            // Coupons are a separate future transactional entitlement source.
            // A zero-price offer is not a verified 100% discount coupon.
            if (price.Amount == 0)
                return new OrderResult(false, "zero_total_requires_coupon_checkout");

            CatalogDiscountCoupon? coupon = null;
            var discount = 0m;
            if (request.CouponCode is not null)
            {
                var result = await CatalogCouponRules.CheckAsync(db,
                    request.CouponCode, tenantId, request.PlanVersionId,
                    price.Amount, nowUtc, forSettlement: true, ct);
                if (result.Coupon is null)
                    return new OrderResult(false, result.Error);
                coupon = result.Coupon;
                discount = CatalogCouponRules.Discount(coupon, price.Amount);
            }
            var net = price.Amount - discount;
            var zeroTotal = net == 0m;
            var order = new TenantPlanOrder
            {
                TenantId = tenantId, PlanVersionId = request.PlanVersionId,
                CouponId = coupon?.Id,
                Status = zeroTotal ? "Settled" : "Pending",
                BillingCycle = request.BillingCycle, Currency = "IRR",
                GrossAmount = price.Amount, DiscountAmount = discount,
                NetAmount = net, QuotedAtUtc = nowUtc,
                IdempotencyKey = request.IdempotencyKey,
                SettledAtUtc = zeroTotal ? nowUtc : null,
                SettlementKind = zeroTotal ? "ZeroTotalCoupon" : null
            };
            if (zeroTotal)
            {
                // The receipt refers to the server-created order itself, not
                // a customer-provided payment / external bank transaction.
                order.SettlementReference = "coupon:" + order.Id.ToString("N");
                db.CatalogCouponRedemptions.Add(new CatalogCouponRedemption
                {
                    CouponId = coupon!.Id, TenantId = tenantId, OrderId = order.Id,
                    DiscountAmount = discount, RedeemedAtUtc = nowUtc
                });
                db.TenantSubscriptions.Add(new TenantSubscription
                {
                    TenantId = tenantId, PlanVersionId = request.PlanVersionId,
                    SourceOrderId = order.Id, Status = "Active",
                    StartsAtUtc = nowUtc,
                    EndsAtUtc = request.BillingCycle == "yearly"
                        ? nowUtc.AddYears(1) : nowUtc.AddMonths(1),
                    ActivatedAtUtc = nowUtc
                });
            }
            db.TenantPlanOrders.Add(order);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new OrderResult(true,
                zeroTotal ? "zero_total_settled_and_activated" : "created_pending",
                View(order));
        });
    }

    private async Task<bool> MatchesCouponAsync(
        Guid? existingCouponId, string? suppliedCode, CancellationToken ct)
    {
        if (existingCouponId is null) return suppliedCode is null;
        if (suppliedCode is null) return false;
        var hash = CatalogCouponRules.Fingerprint(suppliedCode);
        return await db.CatalogDiscountCoupons.AsNoTracking()
            .AnyAsync(x => x.Id == existingCouponId && x.CodeHash == hash, ct);
    }

    public async Task<IReadOnlyList<OrderView>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct)
    {
        var rows = await db.TenantPlanOrders.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.QuotedAtUtc).Take(50).ToArrayAsync(ct);
        return rows.Select(View).ToArray();
    }

    public async Task<IReadOnlyList<OrderView>> ListForSystemAsync(
        string status, CancellationToken ct)
    {
        if (status is not ("Pending" or "Settled" or "Cancelled"))
            return [];
        var rows = await db.TenantPlanOrders.IgnoreQueryFilters(["tenant"])
            .AsNoTracking().Where(x => x.Status == status)
            .OrderByDescending(x => x.QuotedAtUtc).Take(100).ToArrayAsync(ct);
        return rows.Select(View).ToArray();
    }

    /// <summary>
    /// SYSTEM ADMIN operation, AFTER independent out-of-band receipt review.
    /// This is an explicit attestation, not automatic confirmation from a
    /// payment provider. Always quote a positive IRR amount first.
    /// </summary>
    public async Task<OrderResult> AttestExternalSettlementAsync(
        Guid orderId, Guid verifiedBySystemUserId,
        ManualAttestation request, DateTime nowUtc, CancellationToken ct)
    {
        if (orderId == Guid.Empty || verifiedBySystemUserId == Guid.Empty ||
            request is null || !request.PaymentVerified ||
            string.IsNullOrWhiteSpace(request.ReceiptReference) ||
            request.ReceiptReference.Trim().Length is < 6 or > 160 ||
            request.ReceiptReference.Any(char.IsControl))
            return new(false, "invalid_receipt_attestation");
        if (!db.Database.IsSqlServer()) return new(false, "sql_server_required");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var order = await db.TenantPlanOrders.IgnoreQueryFilters(["tenant"])
                .SingleOrDefaultAsync(x => x.Id == orderId, ct);
            if (order is null) return new OrderResult(false, "order_not_found");
            var reference = request.ReceiptReference.Trim();

            if (order.Status == "Settled")
            {
                var existing = await db.TenantSubscriptions.IgnoreQueryFilters(["tenant"])
                    .AsNoTracking().SingleOrDefaultAsync(x => x.SourceOrderId == order.Id, ct);
                return order.SettlementKind == "ExternalManual" &&
                       order.SettlementReference == reference && existing is not null
                    ? new OrderResult(true, "already_settled", View(order), existing.Id)
                    : new OrderResult(false, "already_settled_conflict");
            }
            if (order.Status != "Pending" || order.NetAmount <= 0 ||
                order.Currency != "IRR" || order.QuotedAtUtc > nowUtc ||
                order.GrossAmount != order.NetAmount + order.DiscountAmount ||
                (order.CouponId is null && order.DiscountAmount != 0))
                return new OrderResult(false, "order_not_payable");

            if (!await db.Tenants.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                .AnyAsync(x => x.Id == order.TenantId && x.IsActive, ct))
                return new OrderResult(false, "tenant_inactive");

            if (await db.TenantSubscriptions.IgnoreQueryFilters(["tenant"])
                .AnyAsync(x => x.TenantId == order.TenantId && x.Status == "Active", ct))
                return new OrderResult(false, "active_subscription_exists");

            if (await db.TenantPlanOrders.IgnoreQueryFilters(["tenant"])
                .AsNoTracking().AnyAsync(x =>
                    x.Id != order.Id && x.Status == "Settled" &&
                    x.SettlementKind == "ExternalManual" &&
                    x.SettlementReference == reference, ct))
                return new OrderResult(false, "receipt_reference_already_used");

            try
            {
                var plan = await new PublishedPlanCatalogReader(db).ReadAsync(
                    order.PlanVersionId, resources, capabilities, nowUtc, ct);
                if (plan.ModuleCodes.Count == 0 ||
                    !plan.Prices.Any(x => x.BillingCycle == order.BillingCycle &&
                        x.Currency == order.Currency && x.Amount == order.GrossAmount))
                    return new OrderResult(false, "plan_offer_unavailable");
            }
            catch (InvalidOperationException)
            {
                return new OrderResult(false, "plan_not_saleable");
            }

            if (order.CouponId is Guid couponId)
            {
                var check = await CatalogCouponRules.CheckAsync(db,
                    couponId.ToString("D"), order.TenantId, order.PlanVersionId,
                    order.GrossAmount, nowUtc, forSettlement: true, ct, isId: true);
                if (check.Coupon is null)
                    return new OrderResult(false, check.Error);
                if (CatalogCouponRules.Discount(check.Coupon, order.GrossAmount)
                    != order.DiscountAmount)
                    return new OrderResult(false, "coupon_quote_changed");
                db.CatalogCouponRedemptions.Add(new CatalogCouponRedemption
                {
                    TenantId = order.TenantId, CouponId = couponId,
                    OrderId = order.Id, DiscountAmount = order.DiscountAmount,
                    RedeemedAtUtc = nowUtc
                });
            }

            var end = order.BillingCycle == "yearly"
                ? nowUtc.AddYears(1)
                : nowUtc.AddMonths(1);
            order.Status = "Settled";
            order.SettledAtUtc = nowUtc;
            order.SettlementKind = "ExternalManual";
            order.SettlementReference = reference;
            order.LastModifiedBy = verifiedBySystemUserId;
            order.LastModifiedAt = nowUtc;
            var subscription = new TenantSubscription
            {
                TenantId = order.TenantId, PlanVersionId = order.PlanVersionId,
                SourceOrderId = order.Id, Status = "Active",
                StartsAtUtc = nowUtc, EndsAtUtc = end,
                ActivatedAtUtc = nowUtc
            };
            db.TenantSubscriptions.Add(subscription);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new OrderResult(true, "settled_and_activated",
                View(order), subscription.Id);
        });
    }
}
