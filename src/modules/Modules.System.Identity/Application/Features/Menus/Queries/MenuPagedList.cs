using Application.SharedKernel.Models;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Queries
{
    public record MenuPagedListQuery : PageFilter<Menu, MenuPagedListDto>, IQuery<PagedList<MenuPagedListDto>>
    {

    }
    public record MenuPagedListDto : MapFrom<Menu>, IQueryFilter<Menu>
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string? Category { get; set; }
        public Guid? PMenuId { get; set; }
        public string? PMenu { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public string? Title { get; set; }
        public string? Icon { get; set; }
        public short Order { get; set; }
        public string? Url { get; set; }
        public Guid? PermissionId { get; set; }
        public string? Permission { get; set; }
        public static IQueryable<Menu> Apply(IQueryable<Menu> query, string text)
        {
            var res = query.Where(x => x.Title.Contains(text));
            return res;
        }

        public override void Mapping(Profile profile)
        {
            profile.CreateMap<Menu, MenuPagedListDto>()
                .ForMember(x => x.Category, opt => opt.MapFrom(x => x.Category!.Title))
                .ForMember(x => x.PMenu, opt => opt.MapFrom(x => x.Parent!.Title))
                .ForMember(x => x.Permission, opt => opt.MapFrom(x => x.Permission!.Name));
        }
    }
    public class MenuPagedListQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuPagedListQuery, PagedList<MenuPagedListDto>>
    {
        public async Task<PagedList<MenuPagedListDto>> Handle(MenuPagedListQuery request, CancellationToken cancellationToken)
        {
            var query = context.Menus.AsNoTracking();
            var res = await request.ToPaging(query, mapper);
            return res;
        }
    }
}
