using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Queries
{
    public record MenuCategoryGetMenusQuery : IQuery<List<MenuCategoryGetMenusDto>>
    {
        public string? Text { get; set; }
        public Guid CategoryId { get; set; }
    }
    public record MenuCategoryGetMenusDto : MapFrom<Menu>, IQueryFilter<Menu>
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public Guid? ParentId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public string? Title { get; set; }
        public string? Icon { get; set; }
        public short Order { get; set; }
        public string? Route { get; set; }
        public Guid? PermissionId { get; set; }
        public string? Permission { get; set; }
        public static IQueryable<Menu> Apply(IQueryable<Menu> query, string? text)
        {
            if (!string.IsNullOrEmpty(text))
                query = query.Where(x => x.Title.Contains(text));
            return query;
        }
        public override void Mapping(Profile profile)
        {
            base.Mapping(profile);
        }
    }
    public class MenuCategoryGetMenusQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuCategoryGetMenusQuery, List<MenuCategoryGetMenusDto>>
    {
        public async Task<List<MenuCategoryGetMenusDto>> Handle(MenuCategoryGetMenusQuery request, CancellationToken cancellationToken)
        {
            var query = context.Menus.AsNoTracking().Where(x => x.CategoryId == request.CategoryId);
            var res = MenuCategoryGetMenusDto.Apply(query, request.Text);
            var list = await res.ProjectToListAsync<MenuCategoryGetMenusDto>(mapper.ConfigurationProvider);
            return list;
        }
    }
}
