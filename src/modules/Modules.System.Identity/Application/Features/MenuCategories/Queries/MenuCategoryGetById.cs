using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Queries
{
    public record MenuCategoryGetByIdQuery(Guid Id) : IQuery<MenuCategoryGetByIdDto>;
    public record MenuCategoryGetByIdDto : MapFrom<MenuCategory>
    {
        public Guid Id { get; init; }
        public required string Title { get; init; }
        public short Order { get; set; }
        public bool IsActive { get; set; }
    }
    
    public class MenuCategoryGetByIdQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuCategoryGetByIdQuery, MenuCategoryGetByIdDto>
    {
        public async Task<MenuCategoryGetByIdDto> Handle(MenuCategoryGetByIdQuery request, CancellationToken cancellationToken)
        {
            MenuCategoryGetByIdDto? entity = await context.MenuCategories.Where(x => x.Id == request.Id)
                .ProjectTo<MenuCategoryGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken);
            Guard.Against.NotFound(request.Id, entity);
            return entity;
        }
    }
}
