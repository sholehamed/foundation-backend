using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Queries
{
    public record MenuGetByIdQuery : IQuery<MenuGetByIdDto>
    {
        public Guid Id { get; init; }
        public MenuGetByIdQuery()
        {

        }
        public MenuGetByIdQuery(Guid id)
        {
            Id = id;
        }
    }
    public record MenuGetByIdDto : MapFrom<Menu>
    {
        public Guid Id { get; init; }
        public Guid CategoryId { get; init; }
        public Guid? PMenuId { get; init; }
        public bool IsActive { get; init; }
        public bool IsHidden { get; init; }
        public string Title { get; init; }
        public string? Icon { get; init; }
        public short Order { get; init; }
        public string? Url { get; init; }
        public Guid? PermissionId { get; init; }
    }
    public class MenuGetByIdQueryHandler(IIdentityDbContext context, IMapper mapper) : IQueryHandler<MenuGetByIdQuery, MenuGetByIdDto>
    {
        public async Task<MenuGetByIdDto> Handle(MenuGetByIdQuery request, CancellationToken cancellationToken)
        {
            MenuGetByIdDto? entity = await context.Menus.Where(x => x.Id == request.Id).ProjectTo<MenuGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken);
            Guard.Against.NotFound(request.Id, entity);
            return entity;
        }
    }
}
