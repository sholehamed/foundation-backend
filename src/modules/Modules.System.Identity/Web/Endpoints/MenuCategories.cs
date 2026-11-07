using Modules.System.Identity.Web.Util;
﻿using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Features.MenuCategories.Commands;
using Modules.System.Identity.Application.Features.MenuCategories.Queries;


namespace Modules.System.Identity.Web.Endpoints;

public class MenuCategories : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this).RequireAuthorization("SystemAdministrator")

            .MapGet(MenuCategoryGetList, "list", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-LIST", "menu-management", "MENU-MANAGEMENT.READ", "MenuCategoryGetList"))
            .MapGet(MenuCategoryGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-DETAIL", "menu-management", "MENU-MANAGEMENT.READ", "MenuCategoryGetById"))
            .MapPost(MenuCategoryGetPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-PAGED", "menu-management", "MENU-MANAGEMENT.READ", "MenuCategoryGetPagedList"))
            .MapPost(MenuCategoryCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-CREATE", "menu-management", "MENU-MANAGEMENT.CREATE", "MenuCategoryCreate"))
            .MapPut(MenuCategoryUpdateconfigure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-UPDATE", "menu-management", "MENU-MANAGEMENT.UPDATE", "MenuCategoryUpdate"))
            .MapGet(MenuCategoryGetMenus,"{categoryId}/menus", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-MENUS", "menu-management", "MENU-MANAGEMENT.READ", "MenuCategoryGetMenus"))
            .MapDelete(MenuCategoryDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.CATEGORY-DELETE", "menu-management", "MENU-MANAGEMENT.DELETE", "MenuCategoryDelete"))
        ;
    }
    public async Task<Results<Ok<List<MenuCategoryGetMenusDto>>, BadRequest>> MenuCategoryGetMenus(IDispatcher sender,Guid categoryId,string? text)
    {
        var res = await sender.Query(new MenuCategoryGetMenusQuery { CategoryId=categoryId,Text=text});
        return TypedResults.Ok(res);
    }
    public  async Task<Results<NoContent, BadRequest>> MenuCategoryDelete([FromServices] IDispatcher sender,Guid id)
    {
        await sender.Send(new MenuCategoryDeleteCommand(id));
        return TypedResults.NoContent();
    }
    public  async Task<Results<NoContent, BadRequest>> MenuCategoryUpdate(IDispatcher sender, MenuCategoryUpdateCommand request)
    {
        await sender.Send(request);
        return TypedResults.NoContent();
    }
    public  async Task<Results<Ok<Guid>, BadRequest>> MenuCategoryCreate(IDispatcher sender, MenuCategoryCreateCommand request)
    {
        var res = await sender.Send(request);
        return TypedResults.Ok(res);
    }
    public  async Task<Results<Ok<PagedList<MenuCategoryPagedListDto>>, BadRequest>> MenuCategoryGetPagedList(IDispatcher sender, MenuCategoryPagedListQuery request)
    {
        var res = await sender.Query(request);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Ok<List<MenuCategoryPagedListDto>>, BadRequest>> MenuCategoryGetList(IDispatcher sender)
    {
        var res = await sender.Query(new MenuCategoryListQuery());
        return TypedResults.Ok(res);
    }
    public  async Task<Results<Ok<MenuCategoryGetByIdDto>, NotFound>> MenuCategoryGetById([FromServices]IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new MenuCategoryGetByIdQuery(id),cancellationToken);
        return TypedResults.Ok(res);
    }
}