using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Application.Features.Permissions.Commands;
using Modules.System.Identity.Application.Features.Permissions.Queries;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints
{
    public class Permissions : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            // Deployment-safe migration: remain OFF until all existing platform
            // administrators have reviewed grants and a rollback path.
            var granular = app.Configuration.GetValue<bool>(
                "Identity:PlatformPermissions:EnforcePermissionEndpoints");
            Action<RouteHandlerBuilder> requireCreate = e =>
            {
                if (granular) e.RequireSystemPermission("permissions", "create");
            };
            Action<RouteHandlerBuilder> requireUpdate = e =>
            {
                if (granular) e.RequireSystemPermission("permissions", "update");
            };
            Action<RouteHandlerBuilder> requireDelete = e =>
            {
                if (granular) e.RequireSystemPermission("permissions", "delete");
            };
            Action<RouteHandlerBuilder> requireState = e =>
            {
                if (granular) e.RequireSystemPermission("permissions", "change-state");
            };

            app.MapGroup(this).RequireAuthorization("SystemAdministrator")
                .MapPost(PermissionPagedList, "{resourceId}/pagedList")
                .MapPost(PermissionGlobalPagedList, "pagedList")
                .MapGet(PermissionLookup, "lookup")
                .MapGet(PermissionGetById, "{id:guid}")
                .MapPost(PermissionCreate, configure: requireCreate)
                .MapPut(PermissionUpdate, "{id}", configure: requireUpdate)
                .MapDelete(PermissionDelete, "{id}", configure: requireDelete)
                .MapPatch(PermissionChangeState, "{id}", configure: requireState);
        }

        // Global, SystemAdministrator-protected list for root panel menu pages.
        // The legacy resource-scoped endpoint remains unchanged.
        public async Task<Ok<PagedList<PermissionPagedListDto>>> PermissionGlobalPagedList(
            IDispatcher sender, PermissionPagedListQuery parameter,
            CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = Guid.Empty;
            return TypedResults.Ok(await sender.Query(parameter, cancellationToken));
        }

        public async Task<Ok<List<PermissionlookupDto>>> PermissionLookup(
            IDispatcher sender, string? text, CancellationToken ct)
            => TypedResults.Ok(await sender.Query(new PermissionLookupQuery(text), ct));

        // Use explicit HTTP contracts for expected request failures. Do not
        // swallow unexpected exceptions or turn missing resources into 500s.
        public async Task<IResult> PermissionChangeState(
            IDispatcher sender, PlatformPermissionWriteExecutor write,
            HttpContext http, Guid id)
        {
            try
            {
                await write.ExecuteAsync(http.User, "permissions.change-state",
                    async () => await sender.Send(new PermissionChangeStateCommand(id)),
                    permissionId: id);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (PlatformPermissionRequestException exception) { return KnownFailure(exception); }
        }

        public async Task<IResult> PermissionDelete(
            IDispatcher sender, PlatformPermissionWriteExecutor write,
            HttpContext http, Guid id)
        {
            try
            {
                await write.ExecuteAsync(http.User, "permissions.delete",
                    async () => await sender.Send(new PermissionDeleteCommand(id)),
                    permissionId: id);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (PlatformPermissionRequestException exception) { return KnownFailure(exception); }
        }

        public async Task<Results<Ok<PagedList<PermissionPagedListDto>>, BadRequest>>
            PermissionPagedList(IDispatcher sender, Guid resourceId,
                PermissionPagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = resourceId;
            return TypedResults.Ok(await sender.Query(parameter, cancellationToken));
        }

        public async Task<IResult> PermissionGetById(
            [FromServices] IDispatcher sender, Guid id,
            CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                return TypedResults.BadRequest();
            try
            {
                return TypedResults.Ok(await sender.Query(
                    new PermissionGetByIdQuery(id), cancellationToken));
            }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        }

        public async Task<IResult> PermissionCreate(
            IDispatcher sender, PlatformPermissionWriteExecutor write, HttpContext http,
            PermissionCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Code = parameter.Code?.Trim().ToUpperInvariant()!;
            try
            {
                var id = await write.ExecuteAsync(http.User, "permissions.create",
                    async () => await sender.Send(parameter, cancellationToken),
                    newCode: parameter.Code, cancellationToken: cancellationToken,
                    newName: parameter.Name, resourceId: parameter.ResourceId,
                    links: parameter.PermissionResources);
                return TypedResults.Created(id.ToString(), id);
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (PlatformPermissionRequestException exception) { return KnownFailure(exception); }
        }

        public async Task<IResult> PermissionUpdate(
            [FromServices] IDispatcher sender, PlatformPermissionWriteExecutor write,
            HttpContext http, [FromRoute] Guid id, PermissionUpdateCommand parameter,
            CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            parameter.Code = parameter.Code?.Trim().ToUpperInvariant()!;
            try
            {
                await write.ExecuteAsync(http.User, "permissions.update",
                    async () => await sender.Send(parameter, cancellationToken),
                    newCode: parameter.Code, permissionId: id,
                    cancellationToken: cancellationToken, newName: parameter.Name,
                    resourceId: parameter.ResourceId, links: parameter.PermissionResources);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (PlatformPermissionRequestException exception) { return KnownFailure(exception); }
        }

        private static IResult KnownFailure(PlatformPermissionRequestException error)
            => TypedResults.Problem(
                statusCode: error.StatusCode,
                title: error.StatusCode switch
                {
                    StatusCodes.Status400BadRequest => "Invalid permission request",
                    StatusCodes.Status404NotFound => "Permission not found",
                    StatusCodes.Status409Conflict => "Permission conflict",
                    _ => "Permission operation rejected"
                },
                detail: error.Message);
    }
}
