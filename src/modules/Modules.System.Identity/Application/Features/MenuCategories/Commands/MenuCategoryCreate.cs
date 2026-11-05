using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Commands
{
    public record MenuCategoryCreateCommand : MapTo<MenuCategory>, ICommand<Guid>
    {
        public required string Title { get; set; }
        public short Order { get; set; }
        public bool IsActive { get; set; }
    }
    public class MenuCategoryCreateCommandValidator : AbstractValidator<MenuCategoryCreateCommand>
    {

    }
    public class MenuCategoryCreateCommandHandler(IIdentityDbContext dbContext, IMapper mapper) : ICommandHandler<MenuCategoryCreateCommand, Guid>
    {
        public async Task<Guid> Handle(MenuCategoryCreateCommand command, CancellationToken cancellationToken)
        {
            MenuCategory entity = mapper.Map<MenuCategory>(command);

            await dbContext.MenuCategories.AddAsync(entity,cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }
}
