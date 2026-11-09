using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Web.Util;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;


namespace Modules.System.Identity.Web.Endpoints;

public class Admins : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapPost(AdminPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.PAGED", "admins", "ADMINS.READ", "AdminPagedList"))
            .MapGet(UserGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.DETAIL", "admins", "ADMINS.READ", "UserGetById"))
            .MapGet(IsUsernameTaken, "isUserNameTaken", configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.USERNAME-CHECK", "admins", "ADMINS.READ", "IsUsernameTaken"))
            .MapPost(UserCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.ADMINS.CREATE", "admins", "ADMINS.CREATE", "UserCreate").RequireRateLimiting("platform-account-create"))
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
    // FR-14B-1: the legacy CQRS command accepts arbitrary TenantId and/or
    // password changes. Never dispatch it through a System endpoint.
    // Dedicated scoped, audited account lifecycle APIs are required before
    // ordinary create/edit/delete/state/lock operations can be re-enabled.

    // FR-14B-2A: the only allowed ordinary System account creation is an
    // INACTIVE identity with no login password, role, group or permissions.
    // Admin editor activation remains blocked until the reviewed first-login
    // protocol and deployment migration are ready.
    public async Task<IResult> UserCreate(
        HttpContext http, IdentityDbContext db, AuthorizedPlatformMasterWrite gateway,
        IPasswordHasher<User> hasher, IConfiguration configuration,
        SystemPendingAccountWriter.CreateRequest input,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        // Never accept privileged account mutation on cleartext transport.
        if (!http.Request.IsHttps)
            return Results.Forbid();

        try
        {
            var created = await SystemPendingAccountWriter.CreateAsync(
                db, gateway, hasher, http.User, input,
                configuration.GetValue<bool>(
                    "Identity:PlatformAccounts:PendingCreationEnabled"),
                cancellationToken);
            return Results.Created($"/api/System/Admins/{created.Id}", created);
        }
        catch (SystemAccountCreateValidationException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
        catch (SystemAccountCreateConflictException e)
        {
            return Results.Conflict(new { error = e.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }
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