using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Modules.System.Bff;

/// <summary>
/// Binds the authenticated OIDC principal to the host/context that initiated
/// the challenge. A valid OIDC response for a different tenant is not a BFF session.
/// </summary>
public static class BffSessionContextValidator
{
    /// <summary>
    /// Compatibility overload for callers that still hold a RedirectUri in
    /// AuthenticationProperties. The OIDC TicketReceived event must use the
    /// overload taking TicketReceivedContext.ReturnUri instead, because the
    /// framework clears AuthenticationProperties.RedirectUri first.
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
        error = "The OIDC identity does not match the initiating browser context.";

        if (principal?.Identity?.IsAuthenticated != true ||
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

        if (!Security.BffLocalReturnUrl.TryValidate(returnUri, out _))
            return false;

        error = string.Empty;
        return true;
    }
}
