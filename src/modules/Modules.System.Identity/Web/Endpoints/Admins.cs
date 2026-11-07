using Modules.System.Identity.Application.Features.Tenants.Commands;
using Modules.System.Identity.Application.Features.Users.Commands;
using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Web.Util;


namespace Modules.System.Identity.Web.Endpoints;

public class Admins : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapPost(AdminPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.PAGED", "admins", "ADMINS.READ", "AdminPagedList"))
            .MapGet(UserGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.DETAIL", "admins", "ADMINS.READ", "UserGetById"))
            .MapGet(IsUsernameTaken, "isUserNameTaken", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.USERNAME-CHECK", "admins", "ADMINS.READ", "IsUsernameTaken"))
            .MapPost(UserCreateconfigure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.CREATE", "admins", "ADMINS.CREATE", "UserCreate"))
            .MapPut(UserUpdate, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.UPDATE", "admins", "ADMINS.UPDATE", "UserUpdate"))
            .MapDelete(UserDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.DELETE", "admins", "ADMINS.DELETE", "UserDelete"))
            .MapPatch(UserChangeState, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.CHANGE-STATE", "admins", "ADMINS.CHANGE-STATE", "UserChangeState"))
            .MapPatch(UserChangeLockState, "{id}/changeLock", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.CHANGE-LOCK", "admins", "ADMINS.CHANGE-LOCK", "UserChangeLockState"))
            .MapPut(GrantPlatformUserRole, "{id:guid}/roles/{roleId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.GRANT-ROLE", "admins", "ADMINS.ASSIGN-ROLE", "GrantPlatformUserRole"))
            .MapDelete(RevokePlatformUserRole, "{id:guid}/roles/{roleId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.REVOKE-ROLE", "admins", "ADMINS.ASSIGN-ROLE", "RevokePlatformUserRole"))
            .MapPut(AllowPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/allow", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.ALLOW-PERMISSION", "admins", "ADMINS.ASSIGN-PERMISSION", "AllowPlatformUserPermission"))
            .MapPut(DenyPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/deny", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.DENY-PERMISSION", "admins", "ADMINS.ASSIGN-PERMISSION", "DenyPlatformUserPermission"))
            .MapDelete(RemovePlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.REMOVE-PERMISSION", "admins", "ADMINS.ASSIGN-PERMISSION", "RemovePlatformUserPermission"))

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
    public async Task<Results<Ok<PagedList<UserPagedListDto>>, BadRequest>> AdminPagedList(IDispatcher sender, AdminPagedListQuery parameter, CancellationToken cancellationToken = default)
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