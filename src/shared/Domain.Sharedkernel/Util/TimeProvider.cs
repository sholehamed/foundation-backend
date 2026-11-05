using System.Globalization;

namespace Domain.Sharedkernel.Util
{
    public static class TimeProvider
    {
        public static DateTime Now =>DateTime.Now;
        public static DateTime UtcNow =>DateTime.UtcNow;
        public static DateTimeOffset UtcNowOffset => new DateTimeOffset(
    2026, 7, 22,
    19, 22, 18,
    TimeSpan.Zero
);
        public static DateTimeOffset NowOffset => DateTimeOffset.Now;
    }
}
