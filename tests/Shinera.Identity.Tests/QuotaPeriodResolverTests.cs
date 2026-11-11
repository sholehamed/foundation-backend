using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class QuotaPeriodResolverTests
{
    private static DateTime U(int y, int m, int d,
        int hour = 0, int minute = 0) =>
        new(y, m, d, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Subscription_term_is_one_pinned_utc_interval()
    {
        var start = U(2026, 1, 31, 10);
        var end = U(2027, 1, 31, 10);
        var result = QuotaPeriodResolver.Resolve(start, end, U(2026, 7, 1),
            QuotaPeriodCadence.SubscriptionTerm);
        Assert.NotNull(result);
        Assert.Equal(start, result.StartUtc);
        Assert.Equal(end, result.EndUtc);
    }

    [Fact]
    public void Monthly_anniversaries_are_anchored_and_do_not_drift_after_short_month()
    {
        var start = U(2026, 1, 31, 10);
        var end = U(2026, 5, 15, 10);
        var feb = QuotaPeriodResolver.Resolve(start, end, U(2026, 2, 28, 10),
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Equal(U(2026, 2, 28, 10), feb!.StartUtc);
        Assert.Equal(U(2026, 3, 31, 10), feb.EndUtc);
        var march = QuotaPeriodResolver.Resolve(start, end, U(2026, 3, 31, 10),
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Equal(U(2026, 3, 31, 10), march!.StartUtc);
        Assert.Equal(U(2026, 4, 30, 10), march.EndUtc);
        var final = QuotaPeriodResolver.Resolve(start, end, U(2026, 5, 5, 10),
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Equal(U(2026, 4, 30, 10), final!.StartUtc);
        Assert.Equal(end, final.EndUtc);
    }

    [Fact]
    public void Leap_day_yearly_anniversary_is_computed_from_original_anchor()
    {
        var start = U(2024, 2, 29, 12);
        var end = U(2028, 6, 1);
        var yearOne = QuotaPeriodResolver.Resolve(start, end, U(2025, 2, 28, 12),
            QuotaPeriodCadence.YearlyAnniversary);
        Assert.Equal(U(2025, 2, 28, 12), yearOne!.StartUtc);
        Assert.Equal(U(2026, 2, 28, 12), yearOne.EndUtc);
        var yearFour = QuotaPeriodResolver.Resolve(start, end, U(2028, 2, 29, 12),
            QuotaPeriodCadence.YearlyAnniversary);
        Assert.Equal(U(2028, 2, 29, 12), yearFour!.StartUtc);
        Assert.Equal(end, yearFour.EndUtc);
    }

    [Fact]
    public void Missing_cadence_expired_future_or_ambiguous_timezone_is_not_inferred()
    {
        var start = U(2026, 11, 1);
        var end = U(2026, 12, 1);
        Assert.Null(QuotaPeriodResolver.Resolve(start, end, U(2026, 11, 3), null));
        Assert.Null(QuotaPeriodResolver.Resolve(start, end, U(2026, 10, 31),
            QuotaPeriodCadence.MonthlyAnniversary));
        Assert.Null(QuotaPeriodResolver.Resolve(start, end, end,
            QuotaPeriodCadence.MonthlyAnniversary));
        Assert.Null(QuotaPeriodResolver.Resolve(start, end,
            DateTime.SpecifyKind(U(2026, 11, 3), DateTimeKind.Unspecified),
            QuotaPeriodCadence.SubscriptionTerm));
        Assert.Null(QuotaPeriodResolver.Resolve(end, start, start,
            QuotaPeriodCadence.SubscriptionTerm));
    }

    [Fact]
    public void Calendar_midnight_is_not_assumed_as_the_subscription_anchor()
    {
        var start = U(2026, 6, 18, 17, 30);
        var end = U(2026, 9, 18, 17, 30);
        var before = QuotaPeriodResolver.Resolve(start, end,
            U(2026, 7, 18, 17, 29),
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Equal(start, before!.StartUtc);
        Assert.Equal(U(2026, 7, 18, 17, 30), before.EndUtc);
        var onBoundary = QuotaPeriodResolver.Resolve(start, end,
            U(2026, 7, 18, 17, 30),
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Equal(U(2026, 7, 18, 17, 30), onBoundary!.StartUtc);
    }

    [Fact]
    public void Nonquota_capability_cannot_declare_quota_calendar()
    {
        var resource = new ResourceContract("appointments", "bookings",
            ModuleProductClass.Commercial, ["APPOINTMENTS.READ"]);
        var invalid = new CapabilityContract("appointments.recurring",
            "appointments", "bookings", CapabilityKind.Feature,
            CapabilityValueKind.Boolean, CapabilityMergePolicy.Override,
            QuotaPeriodCadence.MonthlyAnniversary);
        Assert.Contains(ModuleResourceContracts.Validate([resource], [invalid]),
            x => x.Code == "capability_period_invalid");
    }
}
