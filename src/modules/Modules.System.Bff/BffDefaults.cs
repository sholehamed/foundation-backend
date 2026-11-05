namespace Modules.System.Bff;

/// <summary>
/// Stable scheme and cookie names for the browser session boundary.
/// The current implementation slice does not provide interactive login.
/// </summary>
public static class BffDefaults
{
    public const string CookieScheme = "Foundation.Bff";
    public const string CookieName = "__Host-Foundation.Bff";
    public const string TicketCachePrefix = "foundation:bff:session:v1:";
}
