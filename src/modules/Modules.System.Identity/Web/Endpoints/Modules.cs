using Modules.System.Identity.Web.Util;
﻿using Modules.System.Identity.Application.Features.Modules.Commands;
using Modules.System.Identity.Application.Features.Modules.Queries;

namespace Modules.System.Identity.Web.Endpoints
{
    public class Modules: EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapPost(ModulePagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.PAGED", "modules", "MODULES.READ", "ModulePagedList"))
                .MapGet(ModuleGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.DETAIL", "modules", "MODULES.READ", "ModuleGetById"))
                .MapGet(ModuleLookup, "lookup", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.LOOKUP", "modules", "MODULES.READ", "ModuleLookup"))
                .MapPost(ModuleCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.CREATE", "modules", "MODULES.CREATE", "ModuleCreate"))
                .MapPut(ModuleUpdate, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.UPDATE", "modules", "MODULES.UPDATE", "ModuleUpdate"))
                .MapDelete(ModuleDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.DELETE", "modules", "MODULES.DELETE", "ModuleDelete"))
                .MapPatch(ModuleChangeState, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MODULES.CHANGE-STATE", "modules", "MODULES.CHANGE-STATE", "ModuleChangeState"))
            ;
        }
        public async Task ModuleChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new ModuleChangeStateCommand(id));
        }

        public async Task ModuleDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new ModuleDeleteCommand(id));
        }
        public async Task<Results<Ok<List<LookupDto>>, BadRequest>> ModuleLookup(IDispatcher sender, string? text, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new ModuleLookupQuery(text), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<PagedList<ModulePagedListDto>>, BadRequest>> ModulePagedList(IDispatcher sender, ModulePagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<ModuleGetByIdDto>, NotFound>> ModuleGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new ModuleGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> ModuleCreate(IDispatcher sender, ModuleCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> ModuleUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, ModuleUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
