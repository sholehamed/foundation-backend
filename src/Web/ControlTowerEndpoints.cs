using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Web.Util;
using Modules.System.Observability;

namespace Server;

/// <summary>
/// Host-owned read-only System Control Tower boundary. Angular sees only this
/// explicit sanitized DTO, never Tempo access credentials, query language,
/// backend URLs, raw OTLP attributes, SQL text or captured payloads.
/// </summary>
public static class ControlTowerEndpoints
{
    public const string TraceReadPermission = "CONTROL-TOWER.TRACES.READ";

    public static void MapControlTower(this WebApplication app)
    {
        app.MapGet("/api/system/control-tower/traces/{traceId}", ReadTraceAsync)
            .RequireAuthorization("SystemAdministrator")
            .RequireRateLimiting("control-tower-trace-read")
            .WithFeatureEndpoint(
                "SYSTEM.CONTROL-TOWER.TRACE-DETAIL", "control-tower",
                TraceReadPermission, "Read sanitized Control Tower trace");
    }

    public static async Task<IResult> ReadTraceAsync(
        string traceId, HttpContext http, ITenantContext current,
        IIdentityDbContext db, FoundationTempoTraceReader reader,
        ILogger<FoundationTempoTraceReader> logger, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Explicit deny for Tenant and even System accounts without a current
        // live SuperAdmin role AND the separately revocable read permission.
        // Do not rely on the optional global granular System rollout flag.
        if (!current.IsSystemContext || !current.IsSuperAdmin ||
            current.UserId is not Guid actorId || actorId == Guid.Empty)
            return TypedResults.Forbid();

        var owners = await db.Permissions.AsNoTracking()
            .Where(x => x.Code == TraceReadPermission)
            .Select(x => new
            {
                x.IsActive,
                ActiveResource = x.Resource != null && x.Resource.IsActive,
                ResourceCode = x.Resource == null ? null : x.Resource.Code,
                ActiveModule = x.Resource != null && x.Resource.Module != null &&
                               x.Resource.Module.IsActive,
                ModuleCode = x.Resource == null || x.Resource.Module == null ?
                             null : x.Resource.Module.Code
            }).ToListAsync(ct);

        if (owners.Count != 1 || !owners[0].IsActive ||
            !owners[0].ActiveResource || !owners[0].ActiveModule ||
            owners[0].ResourceCode != "control-tower" ||
            owners[0].ModuleCode != "system" ||
            !await new EffectivePermissionEvaluator(db).HasPermissionAsync(
                actorId, DefaultSeedData.MasterTenant.Id,
                UserType.System, TraceReadPermission, ct))
            return TypedResults.Forbid();

        // TraceId is solely a lookup key, never an identity or tenant
        // authorization claim. No caller-controlled service/url/search syntax.
        var response = await reader.ReadAsync(traceId, ct);
        logger.LogInformation(
            "Control Tower sanitized trace lookup completed: {ResultState}",
            response.State.ToString());

        return response.State switch
        {
            FoundationTempoTraceReader.ReadState.Invalid =>
                TypedResults.BadRequest(new { state = "InvalidTraceId" }),
            FoundationTempoTraceReader.ReadState.Missing =>
                TypedResults.NotFound(new { state = "NotRetained" }),
            FoundationTempoTraceReader.ReadState.Unavailable =>
                TypedResults.Problem(statusCode: 503,
                    title: "Telemetry unavailable", detail: "Trace store is not available."),
            _ when response.Data is not null => TypedResults.Ok(response.Data),
            _ => TypedResults.Problem(statusCode: 503, title: "Telemetry unavailable")
        };
    }
}
