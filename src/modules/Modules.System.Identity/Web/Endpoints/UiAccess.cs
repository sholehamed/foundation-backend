using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Read-only SystemAdministrator endpoint for *UI hints*, not a replacement
/// for server-side RequireSystemPermission on protected API operations.
/// </summary>
public sealed class UiAccess : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)
            .RequireAuthorization("SystemAdministrator")
            .MapGet(GetMyUiAccessSnapshot, "me")
            .MapGet(GetMyUiAccess, "me/{key}");
    }

    public static async Task<IResult> GetMyUiAccessSnapshot(
        HttpContext http, IIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await new CurrentSystemUiAccessService(db)
            .GetSnapshotAsync(http.User, cancellationToken);
        return result is null ? Results.Forbid() : Results.Ok(result);
    }

    public static async Task<IResult> GetMyUiAccess(
        HttpContext http, IIdentityDbContext db,
        string key, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var allowed = await new CurrentSystemUiAccessService(db)
            .IsAllowedAsync(http.User, key, cancellationToken);
        return TypedResults.Ok(new SystemUiAccessDecision(key, allowed));
    }
}

public sealed record SystemUiAccessDecision(string Key, bool Allowed);
