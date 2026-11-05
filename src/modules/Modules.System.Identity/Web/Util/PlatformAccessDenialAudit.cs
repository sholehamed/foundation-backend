using System.Security.Claims;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Infrastructure.Persistence;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Captures only a failed SystemAdministrator policy decision for mapped API
/// endpoints. Never stores request URLs, route parameters, query strings,
/// Authorization headers, JWTs, client IPs, payloads or exception messages.
/// An authenticated, validated System principal is the only trusted actor ID.
/// </summary>
public sealed class PlatformAccessDenialAudit(IDbConnection connectionFactory)
{
    public async Task RecordAsync(HttpContext context, bool challenged,
        CancellationToken cancellationToken)
    {
        if (context.GetEndpoint() is not RouteEndpoint route)
            throw new InvalidOperationException(
                "Platform denial audit requires a registered route endpoint.");

        var template = route.RoutePattern.RawText ?? "";
        var method = context.Request.Method.ToUpperInvariant();

        // Bounded server-controlled template, NOT the client's Path or QueryString.
        if (template.Length == 0 || template.Length > 256 ||
            method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"))
            throw new InvalidOperationException("Unsupported platform audit metadata.");

        Guid? actorId = null;
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim("account_type", "system") &&
            !context.User.HasClaim(c => c.Type == "tenant_id") &&
            Guid.TryParse(context.User.FindFirstValue(OpenIddictConstants.Claims.Subject), out var parsed))
        {
            actorId = parsed;
        }

        var id = Guid.NewGuid();
        var master = DefaultSeedData.MasterTenant.Id;
        var outcome = challenged ? "challenge" : "forbidden";

        // A completely separate SQL connection prevents accidental enlistment
        // in the rejected request's DbContext transaction/change tracker.
        // Explicit typed parameters; never concatenate client-controlled data.
        // Use the un-opened DI connection factory. SqlClient may redact the
        // password from an EF DbConnection.ConnectionString after opening it,
        // so never derive a fresh connection from the request's live DbContext.
        await using var connection = new SqlConnection(connectionFactory.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO [dbo].[PlatformAccessDenialAuditEntries]
                ([Id], [TenantId], [ActorUserId], [HttpMethod], [RouteTemplate], [Outcome])
            VALUES
                (@id, @master, @actor, @method, @route, @outcome)
            """, connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@master", SqlDbType.UniqueIdentifier).Value = master;
        command.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value =
            actorId.HasValue ? actorId.Value : DBNull.Value;
        command.Parameters.Add("@method", SqlDbType.NVarChar, 8).Value = method;
        command.Parameters.Add("@route", SqlDbType.NVarChar, 256).Value = template;
        command.Parameters.Add("@outcome", SqlDbType.NVarChar, 16).Value = outcome;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Registers an OnStarting hook ahead of UseAuthorization so both middleware-
/// level 401/403 and endpoint-level Forbid() (e.g. reserved catalog) persist.
/// This hook executes before response headers commit, without reading user
/// URLs, request bodies, headers or tokens and without affecting successful
/// operations. One hook per request prevents duplicate audit rows.
/// </summary>
public sealed class PlatformDeniedAccessAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,
        PlatformAccessDenialAudit audit,
        ILogger<PlatformDeniedAccessAuditMiddleware> logger)
    {
        context.Response.OnStarting(async () =>
        {
            if (context.Response.StatusCode is not (
                    StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden) ||
                context.GetEndpoint() is not RouteEndpoint route ||
                !route.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Any(x => x.Policy == SystemAuthorizationPolicies.Administrator))
                return;

            try
            {
                await audit.RecordAsync(context,
                    context.Response.StatusCode == StatusCodes.Status401Unauthorized,
                    context.RequestAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fail closed: never return a 401/403 with a silently missing
                // audit record when SQL persistence is unavailable.
                // Avoid exception bodies, user paths, tokens, or SQL details.
                logger.LogError("Failed to persist a denied platform authorization decision.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentLength = 0;
            }
        });

        await next(context);
    }
}
