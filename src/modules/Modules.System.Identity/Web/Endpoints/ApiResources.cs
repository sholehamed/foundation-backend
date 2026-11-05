using Modules.System.Identity.Application.Features.ApiResources.Commands;
using Modules.System.Identity.Application.Features.ApiResources.Queries;

namespace Modules.System.Identity.Web.Endpoints
{
    public class ApiResources: EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapPost(ApiResourcePagedList, "{resourceId}/pagedList")
                .MapPost(ApiResourceGlobalPagedList, "pagedList")
                .MapPatch(ApiResourceSync, "{resourceId}/sync")
                .MapGet(ApiResourceGetById, "{id}")
                .MapGet(ApiResourceLookup, "lookup")
                .MapPost(ApiResourceCreate,displayName:"ایجاد api جدید")
                .MapPut(ApiResourceUpdate, "{id}")
                .MapDelete(ApiResourceDelete, "{id}")
                .MapPatch(ApiResourceChangeState, "{id}")
            ;
        }
        // Global, SystemAdministrator-protected list for root panel menu pages.
        // The legacy resource-scoped endpoint remains unchanged.
        public async Task<Ok<PagedList<ApiResourcePagedListDto>>> ApiResourceGlobalPagedList(
            IDispatcher sender, ApiResourcePagedListQuery parameter,
            CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = Guid.Empty;
            return TypedResults.Ok(await sender.Query(parameter, cancellationToken));
        }

        public async Task ApiResourceSync(IDispatcher sender,Guid resourceId)
        {
            await sender.Send(new ApiResourceSyncCommand(resourceId));
        }
        public async Task ApiResourceChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new ApiResourceChangeStateCommand(id));
        }

        public async Task ApiResourceDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new ApiResourceDeleteCommand(id));
        }
        public async Task<Results<Ok<List<LookupDto>>, BadRequest>> ApiResourceLookup(IDispatcher sender, string? text, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new ApiResourceLookupQuery(text), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<PagedList<ApiResourcePagedListDto>>, BadRequest>> ApiResourcePagedList(IDispatcher sender,Guid resourceId ,ApiResourcePagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = resourceId;
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<ApiResourceGetByIdDto>, NotFound>> ApiResourceGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new ApiResourceGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> ApiResourceCreate(IDispatcher sender, ApiResourceCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> ApiResourceUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, ApiResourceUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
