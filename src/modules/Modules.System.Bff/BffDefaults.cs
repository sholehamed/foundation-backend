namespace Modules.System.Bff;

/// <summary>
/// Stable scheme, callback and cookie names for the browser session boundary.
/// </summary>
public static class BffDefaults
{
    public const string CookieScheme = "Foundation.Bff";
    public const string OidcScheme = "Foundation.Bff.Oidc";
    public const string CookieName = "__Host-Foundation.Bff";
    public const string CallbackPath = "/signin-oidc";
    public const string TicketCachePrefix = "foundation:bff:session:v1:";

    internal const string LoginContextProperty = ".bff.login_context";
    internal const string TenantProperty = ".bff.tenant";
    internal const string CallbackUriProperty = ".bff.callback_uri";
}
