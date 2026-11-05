using Modules.System.Identity.Application.Features.Tenants.Commands;
using Modules.System.Identity.Application.Features.Users.Commands;
using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Web.Util;


namespace Modules.System.Identity.Web.Endpoints;

public class Users : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapPost(UserPagedList, "pagedList")
            .MapGet(UserGetById, "{id}")
            .MapGet(IsUsernameTaken, "isUserNameTaken")
            .MapPost(UserCreate)
            .MapPut(UserUpdate, "{id}")
            .MapDelete(UserDelete, "{id}")
            .MapPatch(UserChangeState, "{id}")
            .MapPatch(UserChangeLockState, "{id}/changeLock")
            .MapPut(GrantPlatformUserRole, "{id:guid}/roles/{roleId:guid}")
            .MapDelete(RevokePlatformUserRole, "{id:guid}/roles/{roleId:guid}")
            .MapPut(AllowPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/allow")
            .MapPut(DenyPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/deny")
            .MapDelete(RemovePlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}")

        ;
    }

    // Both routes inherit SystemAdministrator and the service independently
    // revalidates the live actor/target state before changing a grant.
    public async Task<Results<NoContent, ForbidHttpResult>> GrantPlatformUserRole(
        PlatformUserRoleWriteService grants, HttpContext http, Guid id,
        Guid roleId, CancellationToken cancellationToken)
    {
        try
        {
            await grants.ChangeAsync(http.User, id, roleId, assign: true, cancellationToken: cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    public async Task<Results<NoContent, ForbidHttpResult>> RevokePlatformUserRole(
        PlatformUserRoleWriteService grants, HttpContext http, Guid id,
        Guid roleId, CancellationToken cancellationToken)
    {
        try
        {
            await grants.ChangeAsync(http.User, id, roleId, assign: false, cancellationToken: cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    // An explicit user denial supersedes both direct and inherited role/group
    // grants in SystemPermissionHandler. These endpoints only mutate one
    // non-reserved Master UserPermission override at a time.
    public async Task<Results<NoContent, ForbidHttpResult>> AllowPlatformUserPermission(
        PlatformUserPermissionWriteService permissions, HttpContext http,
        Guid id, Guid permissionId, CancellationToken cancellationToken)
    {
        try
        {
            await permissions.ChangeAsync(http.User, id, permissionId,
                isGranted: true, cancellationToken: cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    public async Task<Results<NoContent, ForbidHttpResult>> DenyPlatformUserPermission(
        PlatformUserPermissionWriteService permissions, HttpContext http,
        Guid id, Guid permissionId, CancellationToken cancellationToken)
    {
        try
        {
            await permissions.ChangeAsync(http.User, id, permissionId,
                isGranted: false, cancellationToken: cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    public async Task<Results<NoContent, ForbidHttpResult>> RemovePlatformUserPermission(
        PlatformUserPermissionWriteService permissions, HttpContext http,
        Guid id, Guid permissionId, CancellationToken cancellationToken)
    {
        try
        {
            await permissions.ChangeAsync(http.User, id, permissionId,
                isGranted: null, cancellationToken: cancellationToken);
            return TypedResults.NoContent();
        }
        catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
    }

    public async Task UserDelete(IDispatcher sender, Guid id)
    {
        await sender.Send(new UserDeleteCommand(id));
    }
    public async Task<Results<Ok<bool>, BadRequest>> IsUsernameTaken(IDispatcher sender, [AsParameters] IsUsernameTakenQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Ok<PagedList<UserPagedListDto>>, BadRequest>> UserPagedList(IDispatcher sender, UserPagedListQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<UserGetByIdDto>, NotFound>> UserGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new UserGetByIdQuery(id), cancellationToken);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Created<Guid>, BadRequest>> UserCreate(IDispatcher sender, UserCreateCommand parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Send(parameter, cancellationToken);
        return TypedResults.Created(res.ToString(), res);
    }
    public async Task<NoContent> UserUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, UserUpdateCommand parameter, CancellationToken cancellationToken = default)
    {
        parameter.Id = id;
        await sender.Send(parameter, cancellationToken);
        return TypedResults.NoContent();
    }
    public async Task UserChangeState(IDispatcher sender, Guid id)
    {
        await sender.Send(new UserChangeStateCommand(id));
    }
    public async Task UserChangeLockState(IDispatcher sender, Guid id)
    {
        await sender.Send(new UserChangeLockStateCommand(id));
    }


}