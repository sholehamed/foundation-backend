using Modules.System.Identity.Application.Abstractions;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Middlewares;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http,
        ITenantContext tenantContext,
        ITenantAccessResolver resolver)
    {
        var user = http.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            await next(http);
            return;
        }

        // User identity
        var subject = user.FindFirstValue(
            OpenIddictConstants.Claims.Subject);

        if (!Guid.TryParse(subject, out var userId))
        {
            http.Response.StatusCode = 401;
            return;
        }

        var accountType = user.FindFirstValue("account_type");

        // SYSTEM CONTEXT
        if (accountType == "system")
        {
            if (user.HasClaim(c => c.Type == "tenant_id"))
            {
                http.Response.StatusCode = 401;
                return;
            }

            var isSuperAdmin = user
                .FindAll(OpenIddictConstants.Claims.Role)
                .Any(c => string.Equals(
                    c.Value,
                    "SuperAdmin",
                    StringComparison.OrdinalIgnoreCase));

            tenantContext.InitializeSystem(
                userId,
                isSuperAdmin);

            await next(http);
            return;
        }

        // TENANT CONTEXT
        if (accountType == "tenant")
        {
            var rawTenantId = user.FindFirstValue("tenant_id");

            if (!Guid.TryParse(rawTenantId, out var homeTenantId))
            {
                http.Response.StatusCode = 401;
                return;
            }

            var scope = await resolver.ResolveAsync(
                userId,
                homeTenantId,
                http.RequestAborted);

            if (!scope.Readable.Contains(homeTenantId))
            {
                http.Response.StatusCode = 403;
                return;
            }

            // فعلاً تغییر Tenant توسط Header مجاز نیست.
            if (http.Request.Headers.TryGetValue(
                "X-Tenant-Id", out var requestedTenant))
            {
                if (!Guid.TryParse(requestedTenant.ToString(), out var requestedId)
                    || requestedId != homeTenantId)
                {
                    http.Response.StatusCode = 403;
                    return;
                }
            }

            tenantContext.InitializeTenant(
                userId,
                homeTenantId,
                scope.Readable,
                scope.Writable,
                homeTenantId);

            await next(http);
            return;
        }

        // Unknown account type
        http.Response.StatusCode = 401;
    }
}