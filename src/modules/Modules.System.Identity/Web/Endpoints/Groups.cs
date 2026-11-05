using Groups.System.Identity.Application.Features.Groups.Commands;
using Groups.System.Identity.Application.Features.Groups.Queries;
using Modules.System.Identity.Application.Features.Groups.Commands;
using Modules.System.Identity.Infrastructure.Persistence;
using Modules.System.Identity.Web.Util;
using Microsoft.Extensions.Logging;
using Modules.System.Identity.Application.Features.Groups.Queries;
using Modules.System.Identity.Application.Features.Users.Queries;

namespace Groups.System.Identity.Web.Endpoints
{
    public class Groups : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapPost(GroupPagedList, "pagedList")
                .MapGet(GroupGetById, "{id}")
                .MapPost(GroupMembers, "{groupId:guid}/members")
                .MapGet(GroupLookup, "lookup")
                .MapGet(SearchUserToAddGroup, "searchmembers")
                .MapPost(GroupCreate)
                .MapPut(GroupUpdate, "{id}")
                .MapDelete(GroupDelete, "{id}")
                .MapPatch(GroupChangeState, "{id}")
                .MapPost(AddGroupMembers, "{GroupId}/addmembers")
                .MapDelete(RemoveGroupMember, "{groupId:guid}/members/{userId:guid}")
                .MapPut(GrantGroupRole, "{groupId:guid}/roles/{roleId:guid}")
                .MapDelete(RevokeGroupRole, "{groupId:guid}/roles/{roleId:guid}")
            ;
        }
        // SystemAdministrator is required for both routes, and the service
        // revalidates persisted actor, group, members and role on every request.
        public async Task<Results<NoContent, ForbidHttpResult>> GrantGroupRole(
            PlatformGroupRoleWriteService grants, HttpContext http,
            Guid groupId, Guid roleId, CancellationToken cancellationToken)
        {
            try
            {
                await grants.ChangeAsync(http.User, groupId, roleId,
                    assign: true, cancellationToken: cancellationToken);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
        }

        public async Task<Results<NoContent, ForbidHttpResult>> RevokeGroupRole(
            PlatformGroupRoleWriteService grants, HttpContext http,
            ILogger<Groups> logger, Guid groupId, Guid roleId,
            CancellationToken cancellationToken)
        {
            try
            {
                await grants.ChangeAsync(http.User, groupId, roleId,
                    assign: false, cancellationToken: cancellationToken);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException exception)
            {
                // Fail closed for callers, but retain a structured security
                // warning to diagnose denied operations during real HTTP E2E.
                logger.LogWarning(exception,
                    "Denied System GroupRole revoke for group {GroupId}, role {RoleId}",
                    groupId, roleId);
                return TypedResults.Forbid();
            }
        }

        // Scoped URI-only revoke: groupId and userId are validated against
        // Master storage inside a live, Serializable write transaction.
        public async Task<Results<NoContent, ForbidHttpResult>> RemoveGroupMember(
            PlatformGroupMembershipRemovalService removal,
            HttpContext http, Guid groupId, Guid userId,
            CancellationToken cancellationToken)
        {
            try
            {
                await removal.RevokeAsync(http.User, groupId, userId, cancellationToken);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
        }

        public async Task<Results<NoContent, ForbidHttpResult>> AddGroupMembers(
            IDispatcher sender, PlatformGroupMembershipWriteExecutor write,
            HttpContext http, Guid GroupId, AddGroupMembersCommand parameter,
            CancellationToken cancellationToken)
        {
            parameter.GroupId = GroupId;
            parameter.StorageTenantId = DefaultSeedData.MasterTenant.Id;
            try
            {
                await write.ExecuteAsync(http.User, GroupId, parameter.UserIds,
                    async () => await sender.Send(parameter, cancellationToken),
                    cancellationToken);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
        }
        public async Task<Results<Ok<PagedList<GroupMemberPagedListDto>>, NotFound>> GroupMembers(IDispatcher sender, Guid groupId, GroupMembersPagedListQuery parameter, CancellationToken cancellationToken)
        {
            parameter.GroupId = groupId;
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<List<UserPagedListDto>>, NotFound>> SearchUserToAddGroup(IDispatcher sender, [AsParameters] SearchUserToAddGroupQuery parameter, CancellationToken cancellationToken)
        {
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task GroupChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new GroupChangeStateCommand(id));
        }

        public async Task GroupDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new GroupDeleteCommand(id));
        }
        public async Task<Results<Ok<List<LookupDto>>, BadRequest>> GroupLookup(IDispatcher sender, string? text, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new GroupLookupQuery(text), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<PagedList<GroupPagedListDto>>, BadRequest>> GroupPagedList(IDispatcher sender, GroupPagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<GroupGetByIdDto>, NotFound>> GroupGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new GroupGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> GroupCreate(IDispatcher sender, GroupCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> GroupUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, GroupUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
