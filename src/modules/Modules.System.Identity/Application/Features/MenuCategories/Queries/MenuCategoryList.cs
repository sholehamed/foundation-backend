using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.MenuCategories.Queries
{
    public record MenuCategoryListQuery :  IQuery<List<MenuCategoryPagedListDto>>
    {

    }

    public class MenuCategoryListQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuCategoryListQuery, List<MenuCategoryPagedListDto>>
    {
        public async Task<List<MenuCategoryPagedListDto>> Handle(MenuCategoryListQuery request, CancellationToken cancellationToken)
        {
            var res = await context.MenuCategories.AsNoTracking().ProjectTo<MenuCategoryPagedListDto>(mapper.ConfigurationProvider).ToListAsync();
            return res;
        }
    }
}
