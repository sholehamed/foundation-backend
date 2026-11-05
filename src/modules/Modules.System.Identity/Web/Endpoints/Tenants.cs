using Modules.System.Identity.Application.Features.Tenants.Commands;
using Modules.System.Identity.Application.Features.Tenants.Queries;


namespace Modules.System.Identity.Web.Endpoints;

public class Tenants : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapPost(TenantPagedList, "pagedList")
            .MapGet(TenantResolveBySlug, "resolve", configure: x => x.AllowAnonymous())
            .MapGet(TenantGetById, "{id}")
            .MapPost(TenantCreate)
            .MapPut(TenantUpdate, "{id}")
            .MapDelete(TenantDelete, "{id}")
            .MapPatch(TenantChangeState, "{id}")
        ;
    }
    public async Task<Results<Ok<TenantResloveBySlugDto>, NotFound>> TenantResolveBySlug(IDispatcher sender,HttpContext httpContext)
    {
        var slug = httpContext.Request.Headers["X-Tenant-Domain"].ToString();

        try
        {
            var res = await sender.Query(new TenantResolveBySlugQuery(slug));
            return TypedResults.Ok(res);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }
    public async Task TenantChangeState(IDispatcher sender, Guid id)
    {
        await sender.Send(new TenantChangeStateCommand(id));
    }

    public async Task TenantDelete(IDispatcher sender, Guid id)
    {
        await sender.Send(new TenantDeleteCommand(id));
    }

    public async Task<Results<Ok<PagedList<TenantPagedListDto>>, BadRequest>> TenantPagedList(IDispatcher sender, TenantPagedListQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<TenantGetByIdDto>, NotFound>> TenantGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new TenantGetByIdQuery(id), cancellationToken);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Created<Guid>, BadRequest>> TenantCreate(IDispatcher sender, TenantCreateCommand parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Send(parameter, cancellationToken);
        return TypedResults.Created(res.ToString(), res);
    }
    public async Task<NoContent> TenantUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, TenantUpdateCommand parameter, CancellationToken cancellationToken = default)
    {
        parameter.Id = id;
        await sender.Send(parameter, cancellationToken);
        return TypedResults.NoContent();
    }


}