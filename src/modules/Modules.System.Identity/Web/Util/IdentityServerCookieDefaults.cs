namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Interactive Authorization Server session. This is deliberately separate
/// from both the BFF application session and bearer-token authentication.
/// </summary>
public static class IdentityServerCookieDefaults
{
    public const string AuthenticationScheme = "Foundation.IdentityServer";
    public const string CookieName = "__Host-Foundation.Identity";
}
