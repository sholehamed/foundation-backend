using Application.SharedKernel.Models;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Queries;

public record MenuPagedListQuery : PageFilter<Menu, MenuPagedListDto>, IQuery<PagedList<MenuPagedListDto>>
{
}

public record MenuPagedListDto : MapFrom<Menu>, IQueryFilter<Menu>
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string? Category { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentTitle { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public string? Title { get; set; }
    public string? Icon { get; set; }
    public short Order { get; set; }
    public string? Route { get; set; }
    public string? ExternalUrl { get; set; }
    public Guid? PermissionId { get; set; }
    public string? Permission { get; set; }

    // Legacy JSON read aliases for existing clients of pagedList.
    [Obsolete("Use ParentId (parentId in JSON).")]
    public Guid? PMenuId => ParentId;

    [Obsolete("Use ParentTitle (parentTitle in JSON).")]
    public string? PMenu => ParentTitle;

    [Obsolete("Use Route (route in JSON).")]
    public string? Url => Route;

    public static IQueryable<Menu> Apply(IQueryable<Menu> query, string? text)
        => string.IsNullOrWhiteSpace(text) ? query : query.Where(x => x.Title.Contains(text));

    public override void Mapping(Profile profile)
    {
        profile.CreateMap<Menu, MenuPagedListDto>()
            .ForMember(x => x.Category, opt => opt.MapFrom(x => x.Category!.Title))
            .ForMember(x => x.ParentTitle, opt => opt.MapFrom(x => x.Parent!.Title))
            .ForMember(x => x.Permission, opt => opt.MapFrom(x => x.Permission!.Name));
    }
}

public sealed class MenuPagedListQueryHandler(IIdentityDbContext context, IMapper mapper)
    : IQueryHandler<MenuPagedListQuery, PagedList<MenuPagedListDto>>
{
    public async Task<PagedList<MenuPagedListDto>> Handle(
        MenuPagedListQuery request, CancellationToken cancellationToken)
        => await request.ToPaging(context.Menus.AsNoTracking(), mapper);
}
