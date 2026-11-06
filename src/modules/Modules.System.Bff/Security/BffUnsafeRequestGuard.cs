using Microsoft.AspNetCore.Http;

namespace Modules.System.Bff.Security;

/// <summary>
/// Defense in depth for unsafe browser-cookie requests. CSRF token validation
/// is still mandatory; metadata/Origin checks are not a replacement for it.
/// </summary>
public static class BffUnsafeRequestGuard
{
    public static bool IsSameOrigin(HttpRequest request)
    {
        // Browser BFF cookies are Secure and the configured callbacks are
        // HTTPS. Never accept an unsafe cookie-authenticated write over HTTP.
        if (!request.IsHttps)
            return false;

        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (!string.IsNullOrEmpty(fetchSite) &&
            fetchSite is not ("same-origin" or "none"))
        {
            // In particular, same-site is NOT same-origin: another tenant
            // subdomain must not be able to drive a browser logout.
            return false;
        }

        var expectedOrigin = request.Scheme + "://" + request.Host.Value;
        var origin = request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin))
            return string.Equals(origin, expectedOrigin,
                StringComparison.OrdinalIgnoreCase);

        // Some clients omit Origin. If Referer is present, refuse any other
        // origin. When both headers are absent antiforgery still must pass.
        var referer = request.Headers["Referer"].ToString();
        if (!string.IsNullOrEmpty(referer))
        {
            return Uri.TryCreate(referer, UriKind.Absolute, out var uri) &&
                string.Equals(uri.GetLeftPart(UriPartial.Authority),
                    expectedOrigin, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
