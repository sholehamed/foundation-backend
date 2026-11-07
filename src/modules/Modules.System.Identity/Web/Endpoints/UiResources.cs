using Modules.System.Identity.Web.Util;
﻿using Modules.System.Identity.Application.Features.UiResources.Commands;
using Modules.System.Identity.Application.Features.UiResources.Queries;

namespace Modules.System.Identity.Web.Endpoints
{
    public class UiResources: EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapPost(UiResourcePagedList, "{resourceId}/pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.RESOURCE-PAGED", "ui-resources", "UI-RESOURCES.READ", "UiResourcePagedList"))
                .MapPost(UiResourceGlobalPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.PAGED", "ui-resources", "UI-RESOURCES.READ", "UiResourceGlobalPagedList"))
                .MapGet(UiResourceGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.DETAIL", "ui-resources", "UI-RESOURCES.READ", "UiResourceGetById"))
                .MapGet(UiResourceLookup, "lookup", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.LOOKUP", "ui-resources", "UI-RESOURCES.READ", "UiResourceLookup"))
                .MapPost(UiResourceCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.CREATE", "ui-resources", "UI-RESOURCES.CREATE", "UiResourceCreate"))
                .MapPut(UiResourceUpdate, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.UPDATE", "ui-resources", "UI-RESOURCES.UPDATE", "UiResourceUpdate"))
                .MapDelete(UiResourceDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.DELETE", "ui-resources", "UI-RESOURCES.DELETE", "UiResourceDelete"))
                .MapPatch(UiResourceChangeState, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.UI-RESOURCES.CHANGE-STATE", "ui-resources", "UI-RESOURCES.CHANGE-STATE", "UiResourceChangeState"))
            ;
        }
        // Global, SystemAdministrator-protected list for root panel menu pages.
        // The legacy resource-scoped endpoint remains unchanged.
        public async Task<Ok<PagedList<UiResourcePagedListDto>>> UiResourceGlobalPagedList(
            IDispatcher sender, UiResourcePagedListQuery parameter,
            CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = Guid.Empty;
            return TypedResults.Ok(await sender.Query(parameter, cancellationToken));
        }

        public async Task UiResourceChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new UiResourceChangeStateCommand(id));
        }

        public async Task UiResourceDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new UiResourceDeleteCommand(id));
        }
        public async Task<Results<Ok<List<LookupDto>>, BadRequest>> UiResourceLookup(IDispatcher sender, string? text, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new UiResourceLookupQuery(text), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<PagedList<UiResourcePagedListDto>>, BadRequest>> UiResourcePagedList(IDispatcher sender,Guid resourceId, UiResourcePagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            parameter.ResourceId = resourceId;

            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<UiResourceGetByIdDto>, NotFound>> UiResourceGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new UiResourceGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> UiResourceCreate(IDispatcher sender, UiResourceCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> UiResourceUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, UiResourceUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
