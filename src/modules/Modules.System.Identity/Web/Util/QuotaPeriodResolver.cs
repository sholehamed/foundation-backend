namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Exact UTC [Start, End) quota window. Never accept a browser-selected
/// period or infer a cadence from a capability's name.
/// </summary>
public sealed record QuotaPeriodWindow(
    DateTime StartUtc, DateTime EndUtc, QuotaPeriodCadence Cadence);

/// <summary>
/// Source-owned subscription-anchored quota periods. Each anniversary is
/// computed from the ORIGINAL subscription start, not the previous shortened
/// month: Jan 31 -> Feb 28 -> Mar 31 (no cumulative drift).
/// </summary>
public static class QuotaPeriodResolver
{
    public static QuotaPeriodWindow? Resolve(
        DateTime subscriptionStartsUtc,
        DateTime subscriptionEndsUtc,
        DateTime nowUtc,
        QuotaPeriodCadence? cadence)
    {
        if (!cadence.HasValue || !Enum.IsDefined(cadence.Value) ||
            subscriptionStartsUtc.Kind != DateTimeKind.Utc ||
            subscriptionEndsUtc.Kind != DateTimeKind.Utc ||
            nowUtc.Kind != DateTimeKind.Utc ||
            subscriptionStartsUtc >= subscriptionEndsUtc ||
            nowUtc < subscriptionStartsUtc || nowUtc >= subscriptionEndsUtc)
            return null;

        if (cadence == QuotaPeriodCadence.SubscriptionTerm)
            return new(subscriptionStartsUtc, subscriptionEndsUtc, cadence.Value);

        // The elapsed calendar index is approximate around the anniversary
        // day; correct against the original subscription anchor below.
        var index = cadence == QuotaPeriodCadence.MonthlyAnniversary
            ? (nowUtc.Year - subscriptionStartsUtc.Year) * 12 +
              nowUtc.Month - subscriptionStartsUtc.Month
            : nowUtc.Year - subscriptionStartsUtc.Year;
        index = Math.Max(0, index);

        DateTime Anniversary(int i)
        {
            if (i == 0) return subscriptionStartsUtc;
            try
            {
                return cadence == QuotaPeriodCadence.MonthlyAnniversary
                    ? subscriptionStartsUtc.AddMonths(i)
                    : subscriptionStartsUtc.AddYears(i);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            }
        }

        while (index > 0 && Anniversary(index) > nowUtc) index--;
        while (Anniversary(index + 1) <= nowUtc) index++;

        var start = Anniversary(index);
        var next = Anniversary(index + 1);
        var end = next < subscriptionEndsUtc ? next : subscriptionEndsUtc;
        return start < end
            ? new(start, end, cadence.Value)
            : null;
    }
}
