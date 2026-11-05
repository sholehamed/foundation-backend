using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Queries
{
    public record MenuCategoryPagedListQuery : PageFilter<MenuCategory, MenuCategoryPagedListDto>, IQuery<PagedList<MenuCategoryPagedListDto>>
    {

    }
    public record MenuCategoryPagedListDto : MapFrom<MenuCategory>, IQueryFilter<MenuCategory>
    {
        public Guid Id { get; init; }
        public required string  Title { get; set; }
        public short Order { get; set; }
        public bool IsActive { get; set; }
        public static IQueryable<MenuCategory> Apply(IQueryable<MenuCategory> query, string text)
        {
            var res = query.Where(x => x.Title.Contains(text));
            return res;
        }
    }
    public class MenuCategoryPagedListQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuCategoryPagedListQuery, PagedList<MenuCategoryPagedListDto>>
    {
        public async Task<PagedList<MenuCategoryPagedListDto>> Handle(MenuCategoryPagedListQuery request, CancellationToken cancellationToken)
        {
            var query = context.MenuCategories.AsNoTracking();
            var res = await request.ToPaging(query, mapper);
            return res;
        }
    }
}
