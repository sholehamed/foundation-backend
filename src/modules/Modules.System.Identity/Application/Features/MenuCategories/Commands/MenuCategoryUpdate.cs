using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Commands
{
    public record MenuCategoryUpdateCommand : MapTo<MenuCategory>, ICommand
    {
        public Guid Id { get; set; }
        public required string Title { get; set; }
        public short Order { get; set; }
        public bool IsActive { get; set; }
    }
    public class MenuCategoryUpdateCommandValidator : AbstractValidator<MenuCategoryUpdateCommand>
    {

    }
    public class MenuCategoryUpdateCommandHandler(IIdentityDbContext context, IMapper mapper) : ICommandHandler<MenuCategoryUpdateCommand>
    {
        public async Task Handle(MenuCategoryUpdateCommand command, CancellationToken cancellationToken)
        {
            MenuCategory? entity = await context.MenuCategories
      .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            mapper.Map(command, entity);

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
