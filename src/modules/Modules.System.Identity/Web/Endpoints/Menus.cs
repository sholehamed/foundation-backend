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
            .MapPost(ReorderMenus, "reorder", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-REORDER", "menu-management", "MENU-MANAGEMENT.REORDER", "ReorderMenus"))
            .MapGet(GetUserMenus, "getUserMenus", configure: e => { e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.LEGACY-ROLE-NAV", "menu-management", "MENU-MANAGEMENT.READ", "GetUserMenus"); e.RequireAuthorization(); })
            .MapGet(MenuGetById, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-DETAIL", "menu-management", "MENU-MANAGEMENT.READ", "MenuGetById"))
            .MapPost(MenuGetPagedList, "pagedList", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-PAGED", "menu-management", "MENU-MANAGEMENT.READ", "MenuGetPagedList"))
            .MapPost(MenuCreate, configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-CREATE", "menu-management", "MENU-MANAGEMENT.CREATE", "MenuCreate"))
            .MapPut(MenuUpdate, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-UPDATE", "menu-management", "MENU-MANAGEMENT.UPDATE", "MenuUpdate"))
            .MapDelete(MenuDelete, "{id}", configure: e => e.WithFeatureEndpoint("SYSTEM.MENU-MANAGEMENT.MENU-DELETE", "menu-management", "MENU-MANAGEMENT.DELETE", "MenuDelete"))
        ;
    }
    public static async Task<IResult> GetMyNavigation(
        HttpContext http,
        global::Modules.System.Identity.Infrastructure.Persistence.Contexts.IdentityDbContext db,
        ITenantContext current,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        // Tenant navigation must also pass the live Tenant PAGE evaluator.
        // Never expose arbitrary global menu routes or Tenant PAGE titles
        // simply because the Menu.PermissionId is null/stale.
        var result = http.User.FindFirst("account_type")?.Value == "tenant"
            ? await TenantWorkspaceNavigationService.GetAsync(
                current, db, http.User, cancellationToken)
            : await new CurrentUserNavigationService(db)
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