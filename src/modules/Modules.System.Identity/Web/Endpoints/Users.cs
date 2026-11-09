using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Web.Util;


namespace Modules.System.Identity.Web.Endpoints;

public class Users : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapPost(UserPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.PAGED", "users", "USERS.READ", "List users"))
            .MapGet(UserGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.DETAIL", "users", "USERS.READ", "User details"))
            .MapGet(IsUsernameTaken, "isUserNameTaken", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.USERNAME-CHECK", "users", "USERS.READ", "Check username"))
            .MapPost(UserCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.CREATE", "users", "USERS.CREATE", "Create user"))
            .MapPut(UserUpdate, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.UPDATE", "users", "USERS.UPDATE", "Update user"))
            .MapDelete(UserDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.DELETE", "users", "USERS.DELETE", "Delete user"))
            .MapPatch(UserChangeState, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.CHANGE-STATE", "users", "USERS.CHANGE-STATE", "Change user state"))
            .MapPatch(UserChangeLockState, "{id}/changeLock", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.CHANGE-LOCK", "users", "USERS.CHANGE-LOCK", "Change user lock state"))
            .MapPut(GrantPlatformUserRole, "{id:guid}/roles/{roleId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.GRANT-ROLE", "users", "USERS.ASSIGN-ROLE", "Grant user role"))
            .MapDelete(RevokePlatformUserRole, "{id:guid}/roles/{roleId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.REVOKE-ROLE", "users", "USERS.ASSIGN-ROLE", "Revoke user role"))
            .MapPut(AllowPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/allow", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.ALLOW-PERMISSION", "users", "USERS.ASSIGN-PERMISSION", "Allow user permission"))
            .MapPut(DenyPlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}/deny", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.DENY-PERMISSION", "users", "USERS.ASSIGN-PERMISSION", "Deny user permission"))
            .MapDelete(RemovePlatformUserPermission, "{id:guid}/permissions/{permissionId:guid}", configure: e => e.WithFeatureEndpoint("SYSTEM.USERS.REMOVE-PERMISSION", "users", "USERS.ASSIGN-PERMISSION", "Remove user permission override"))

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

    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.
    public ForbidHttpResult UserDelete(Guid id) => TypedResults.Forbid();
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
    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.
    public ForbidHttpResult UserCreate() => TypedResults.Forbid();
    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.
    public ForbidHttpResult UserUpdate(Guid id) => TypedResults.Forbid();
    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.
    public ForbidHttpResult UserChangeState(Guid id) => TypedResults.Forbid();
    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.
    public ForbidHttpResult UserChangeLockState(Guid id) => TypedResults.Forbid();


}