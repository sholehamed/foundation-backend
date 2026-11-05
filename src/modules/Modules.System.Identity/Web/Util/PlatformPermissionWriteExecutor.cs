using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.Permissions.Commands;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using OpenIddict.Abstractions;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Bounded, live-authorized System Permission CRUD. Explicit request validation
/// prevents malformed references and duplicate codes from becoming HTTP 500s.
/// This intentionally does not widen the trusted Master write scope.
/// </summary>
public sealed class PlatformPermissionWriteExecutor(
    AuthorizedPlatformMasterWrite gateway,
    IIdentityDbContext database,
    ILogger<PlatformPermissionWriteExecutor> logger)
{
    public async Task<T> ExecuteAsync<T>(
        ClaimsPrincipal principal,
        string operation,
        Func<Task<T>> execute,
        string? newCode = null,
        Guid? permissionId = null,
        CancellationToken cancellationToken = default,
        string? newName = null,
        Guid? resourceId = null,
        ICollection<PermissionResource>? links = null)
    {
        // Never resolve request details before the live System/Master gate.
        using var authorized = await gateway.BeginAsync(principal,
            cancellationToken: cancellationToken);

        if (permissionId.HasValue)
        {
            if (permissionId == Guid.Empty)
                throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                    "A valid permission identifier is required.");

            var currentCode = await database.Permissions.AsNoTracking()
                .Where(x => x.Id == permissionId.Value)
                .Select(x => x.Code).SingleOrDefaultAsync(cancellationToken);

            if (currentCode is null)
                throw new PlatformPermissionRequestException(StatusCodes.Status404NotFound,
                    "Permission not found.");
            if (PlatformPermissionCatalog.IsReservedCode(currentCode))
                throw new UnauthorizedAccessException(
                    "Reserved platform permission definitions cannot be mutated.");
        }

        // Reserved code checks apply to ALL callers, including existing internal
        // service use without an HTTP DTO. Shape validation only applies when
        // an HTTP request passes its name/resource/link payload to this gateway.
        if (PlatformPermissionCatalog.IsReservedCode(newCode))
            throw new UnauthorizedAccessException(
                "Reserved platform permission codes are deployment managed.");

        if (newName is not null || resourceId.HasValue || links is not null)
        {
            var code = newCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 100 ||
                code.Any(char.IsWhiteSpace))
                throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                    "A non-empty permission code of at most 100 characters without whitespace is required.");

            if (PlatformPermissionCatalog.IsReservedCode(code))
                throw new UnauthorizedAccessException(
                    "Reserved platform permission codes are deployment managed.");

            if (string.IsNullOrWhiteSpace(newName) || newName.Trim().Length > 200)
                throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                    "A permission name of at most 200 characters is required.");

            if (!resourceId.HasValue || resourceId == Guid.Empty ||
                !await database.Resources.AsNoTracking()
                    .AnyAsync(x => x.Id == resourceId.Value && x.IsActive, cancellationToken))
                throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                    "A valid active permission resource is required.");

            // The legacy validator is not automatically invoked by IDispatcher.
            // Check normalized duplicates at the actual HTTP write boundary.
            if (await database.Permissions.AsNoTracking().AnyAsync(x =>
                x.Code.ToUpper() == code &&
                (!permissionId.HasValue || x.Id != permissionId.Value), cancellationToken))
                throw new PlatformPermissionRequestException(StatusCodes.Status409Conflict,
                    "Permission code already exists.");

            if (links is not null)
            {
                var apiIds = new HashSet<Guid>();
                var uiIds = new HashSet<Guid>();
                foreach (var link in links)
                {
                    if (link is null || link.Id == Guid.Empty)
                        throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                            "Permission resource links must have a valid identifier.");
                    if (link.Type == "API")
                        apiIds.Add(link.Id);
                    else if (link.Type == "UI")
                        uiIds.Add(link.Id);
                    else
                        throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                            "Permission resource links must be API or UI.");
                }

                if (apiIds.Count > 0 && await database.ApiResources.AsNoTracking()
                    .CountAsync(x => apiIds.Contains(x.Id), cancellationToken) != apiIds.Count)
                    throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                        "Unknown API resource link.");
                if (uiIds.Count > 0 && await database.UiResources.AsNoTracking()
                    .CountAsync(x => uiIds.Contains(x.Id), cancellationToken) != uiIds.Count)
                    throw new PlatformPermissionRequestException(StatusCodes.Status400BadRequest,
                        "Unknown UI resource link.");
            }
        }

        var actor = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        logger.LogInformation("Platform permission operation {Operation} by {ActorId} for {PermissionId}",
            operation, actor, permissionId);
        try
        {
            var result = await execute();
            logger.LogInformation("Platform permission operation {Operation} completed by {ActorId}",
                operation, actor);
            return result;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 or 2601 or 2627 })
        {
            // FK/unique conflicts only; do not remap unrecognized database errors.
            logger.LogWarning(exception,
                "Platform permission reference/uniqueness conflict for {Operation}, actor {ActorId}",
                operation, actor);
            throw new PlatformPermissionRequestException(StatusCodes.Status409Conflict,
                "Permission conflicts with an existing reference or code.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Platform permission operation {Operation} failed for {ActorId}",
                operation, actor);
            throw;
        }
    }

    public Task ExecuteAsync(
        ClaimsPrincipal principal, string operation, Func<Task> execute,
        string? newCode = null, Guid? permissionId = null,
        CancellationToken cancellationToken = default,
        string? newName = null, Guid? resourceId = null,
        ICollection<PermissionResource>? links = null)
        => ExecuteAsync(principal, operation, async () =>
        {
            await execute();
            return true;
        }, newCode, permissionId, cancellationToken, newName, resourceId, links);
}

/// <summary>A known, non-sensitive HTTP status for a malformed or conflicting write.</summary>
public sealed class PlatformPermissionRequestException(int statusCode, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
