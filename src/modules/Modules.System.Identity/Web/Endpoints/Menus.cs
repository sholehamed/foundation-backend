using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Features.Menus.Commands;
using Modules.System.Identity.Application.Features.Menus.Queries;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Web.Util;


namespace Modules.System.Identity.Web.Endpoints;

public class Menus : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        // User-scoped endpoint is intentionally NOT part of the
        // SystemAdministrator-only menu administration group.
        app.MapGroup(this).RequireAuthorization()
            .MapGet(GetMyNavigation, "me");

        app.MapGroup(this).RequireAuthorization("SystemAdministrator")
            .MapPost(ReorderMenus, "reorder")
            .MapGet(GetUserMenus, "getUserMenus", configure: x => x.RequireAuthorization())
            .MapGet(MenuGetById, "{id}")
            .MapPost(MenuGetPagedList, "pagedList")
            .MapPost(MenuCreate)
            .MapPut(MenuUpdate, "{id}")
            .MapDelete(MenuDelete, "{id}")
        ;
    }
    public static async Task<IResult> GetMyNavigation(
        HttpContext http,
        IIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await new CurrentUserNavigationService(db)
            .GetAsync(http.User, cancellationToken);
        return result is null ? Results.Forbid() : Results.Ok(result);
    }

    public async Task ReorderMenus(IDispatcher sender,MenuReorderCommand request,CancellationToken ct)
    {
        await sender.Send(request, ct);
    }
    public  async Task<Results<NoContent, BadRequest>> MenuDelete([FromServices] IDispatcher sender, Guid id)
    {
        await sender.Send(new MenuDeleteCommand(id));
        return TypedResults.NoContent();
    }
    public  async Task<Results<NoContent, BadRequest>> MenuUpdate(IDispatcher sender, Guid id, MenuUpdateCommand request)
    {
        request.Id = id;
        await sender.Send(request);
        return TypedResults.NoContent();
    }
    public  async Task<Results<Ok<Guid>, BadRequest>> MenuCreate(IDispatcher sender, MenuCreateCommand request)
    {
        var res = await sender.Send(request);
        return TypedResults.Ok(res);
    }
    public  async Task<Results<Ok<PagedList<MenuPagedListDto>>, BadRequest>> MenuGetPagedList(IDispatcher sender, MenuPagedListQuery request)
    {
        var res = await sender.Query(request);
        return TypedResults.Ok(res);
    }
    public   async Task<Results<Ok<MenuGetByIdDto>, NotFound>> MenuGetById([FromServices]IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new MenuGetByIdQuery(id),cancellationToken);
        return TypedResults.Ok(res);
    }
    public  async Task<Results<Ok<List<MenuCategoryDto>>, BadRequest>> GetUserMenus(IDispatcher sender, [AsParameters] GetUserMenusQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }




}