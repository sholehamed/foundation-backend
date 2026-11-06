using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Modules.System.Bff;

/// <summary>
/// Validates account type and tenant against the exact browser host that owns
/// a BFF session. Callback validation additionally validates the return URI.
/// This is host binding, not a substitute for live database access validation.
/// </summary>
public static class BffSessionContextValidator
{
    /// <summary>
    /// Compatibility overload for code that still stores RedirectUri in its
    /// AuthenticationProperties. OIDC OnTicketReceived must use ReturnUri.
    /// </summary>
    public static bool TryValidate(
        ClaimsPrincipal? principal,
        AuthenticationProperties? properties,
        HttpRequest request,
        BffOidcSettings settings,
        out string error)
        => TryValidate(
            principal, properties, request, settings, properties?.RedirectUri, out error);

    public static bool TryValidate(
        ClaimsPrincipal? principal,
        AuthenticationProperties? properties,
        HttpRequest request,
        BffOidcSettings settings,
        string? returnUri,
        out string error)
    {
        if (!TryValidateActiveSession(principal, properties, request, settings, out error))
            return false;

        if (!Security.BffLocalReturnUrl.TryValidate(returnUri, out _))
        {
            error = "The OIDC return path is not local to the BFF host.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates the persisted BFF ticket on every browser session read.
    /// A callback's RedirectUri is not available on subsequent requests.
    /// Rejects unrecognized hosts, mismatched tenant/account claims and
    /// callbacks that are not explicitly registered for the current origin.
    /// </summary>
    public static bool TryValidateActiveSession(
        ClaimsPrincipal? principal,
        AuthenticationProperties? properties,
        HttpRequest request,
        BffOidcSettings settings,
        out string error)
    {
        error = "The BFF session does not match the request host or account.";

        if (principal?.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value) ||
            properties is null ||
            !properties.Items.TryGetValue(BffDefaults.LoginContextProperty, out var expectedContext) ||
            expectedContext is not ("system" or "tenant") ||
            !properties.Items.TryGetValue(BffDefaults.CallbackUriProperty, out var expectedCallback) ||
            !settings.IsRegisteredRedirectUri(expectedCallback) ||
            !BffHostContextResolver.TryResolve(request.Host.Host, settings, out var currentHost))
        {
            return false;
        }

        var actualCallback = UriHelper.BuildAbsolute(
            request.Scheme,
            request.Host,
            request.PathBase,
            BffDefaults.CallbackPath);

        if (!string.Equals(actualCallback, expectedCallback, StringComparison.Ordinal) ||
            !string.Equals(currentHost.LoginContext, expectedContext, StringComparison.Ordinal) ||
            !string.Equals(
                principal.FindFirst("account_type")?.Value,
                expectedContext,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedContext == "system")
        {
            if (principal.HasClaim(c => c.Type is "tenant" or "tenant_id"))
                return false;
        }
        else
        {
            if (!properties.Items.TryGetValue(BffDefaults.TenantProperty, out var expectedTenant) ||
                string.IsNullOrWhiteSpace(expectedTenant) ||
                !string.Equals(expectedTenant, currentHost.TenantSlug, StringComparison.Ordinal) ||
                !string.Equals(
                    principal.FindFirst("tenant")?.Value,
                    expectedTenant,
                    StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out _))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
