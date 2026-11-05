using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Infrastructure.Persistence;
﻿using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Features.Roles.Commands;
using Modules.System.Identity.Application.Features.Roles.Queries;
using Roles.System.Identity.Application.Features.Roles.Commands;
using Roles.System.Identity.Application.Features.Roles.Queries;


namespace Modules.System.Identity.Web.Endpoints;

public class Roles : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        // Keep coarse, live SystemAdministrator enforcement for every endpoint.
        // Opt-in granular checks only after a reviewed Master-scope grant migration.
        // Production defaults OFF to preserve existing administrators.
        var granular = app.Configuration.GetValue<bool>(
            "Identity:PlatformPermissions:EnforceRoleEndpoints");
        Action<RouteHandlerBuilder> requireCreate = endpoint =>
        {
            if (granular) endpoint.RequireSystemPermission("roles", "create");
        };
        Action<RouteHandlerBuilder> requireUpdate = endpoint =>
        {
            if (granular) endpoint.RequireSystemPermission("roles", "update");
        };
        Action<RouteHandlerBuilder> requireDelete = endpoint =>
        {
            if (granular) endpoint.RequireSystemPermission("roles", "delete");
        };
        Action<RouteHandlerBuilder> requireState = endpoint =>
        {
            if (granular) endpoint.RequireSystemPermission("roles", "change-state");
        };
        Action<RouteHandlerBuilder> requireAssignments = endpoint =>
        {
            if (granular) endpoint.RequireSystemPermission("roles", "assign-permissions");
        };

        app.MapGroup(this).RequireAuthorization("SystemAdministrator")
             .MapPost(RolePagedList, "pagedList")
                .MapGet(RoleGetById, "{id}")
            .MapGet(RolesLookup, "lookup", configure: x => x.RequireAuthorization())
            .MapGet(RolePermissionsTree, "{roleId}/permissions/tree", configure: x => x.RequireAuthorization())
           .MapPost(RoleCreate, configure: requireCreate)
                .MapPut(RoleUpdate, "{id}", configure: requireUpdate)
                .MapDelete(RoleDelete, "{id}", configure: requireDelete)
                .MapPatch(RoleChangeState, "{id}", configure: requireState)
                .MapPut(UpdateRolePermissions, "{roleId:guid}/permissions", configure: requireAssignments)
        ;
    }
    public async Task<Results<NoContent, NotFound, BadRequest, ForbidHttpResult>> UpdateRolePermissions(IDispatcher sender, PlatformRoleWriteExecutor write, HttpContext http, Guid roleId, UpdateRolePermissionsCommand command)
    {
        command.RoleId = roleId;
        command.StorageTenantId = DefaultSeedData.MasterTenant.Id;
        try
        {
            await write.ExecuteAsync(http.User, "roles.permissions.replace",
                async () => await sender.Send(command), roleId,
                expectedPermissionIds: command.PermissionIds);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }
    public async Task<Results<Ok<RolePermissionTreeResponseDto>, BadRequest>> RolePermissionsTree(IDispatcher sender, Guid roleId)
    {
        var res = await sender.Query(new RolePermissionsTreeQuery { RoleId = roleId });
        return TypedResults.Ok(res);
    }
    public async Task<Results<NoContent, ForbidHttpResult>> RoleChangeState(IDispatcher sender, PlatformRoleWriteExecutor write, HttpContext http, Guid id)
    {
        try
        {
            await write.ExecuteAsync(http.User, "roles.state.change",
                async () => await sender.Send(new RoleChangeStateCommand(id)), id);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    public async Task<Results<NoContent, ForbidHttpResult>> RoleDelete(IDispatcher sender, PlatformRoleWriteExecutor write, HttpContext http, Guid id)
    {
        try
        {
            await write.ExecuteAsync(http.User, "roles.delete",
                async () => await sender.Send(new RoleDeleteCommand(id)), id);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }
    public async Task<Results<Ok<List<RoleLookupDto>>, NotFound>> RolesLookup(IDispatcher sender, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new RoleLookupQuery(), cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<PagedList<RolePagedListDto>>, BadRequest>> RolePagedList(IDispatcher sender, RolePagedListQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<RoleGetByIdDto>, NotFound>> RoleGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new RoleGetByIdQuery(id), cancellationToken);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Created<Guid>, BadRequest, ForbidHttpResult>> RoleCreate(IDispatcher sender, PlatformRoleWriteExecutor write, HttpContext http, RoleCreateCommand parameter, CancellationToken cancellationToken = default)
    {
        parameter.StorageTenantId = DefaultSeedData.MasterTenant.Id;
        try
        {
            var res = await write.ExecuteAsync(http.User, "roles.create",
                async () => await sender.Send(parameter, cancellationToken),
                cancellationToken: cancellationToken, newRoleName: parameter.Name);
            return TypedResults.Created(res.ToString(), res);
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }
    public async Task<Results<NoContent, ForbidHttpResult>> RoleUpdate([FromServices] IDispatcher sender, PlatformRoleWriteExecutor write, HttpContext http, [FromRoute] Guid id, RoleUpdateCommand parameter, CancellationToken cancellationToken = default)
    {
        parameter.Id = id;
        try
        {
            await write.ExecuteAsync(http.User, "roles.update",
                async () => await sender.Send(parameter, cancellationToken),
                id, cancellationToken, newRoleName: parameter.Name);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }


}