using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.MenuCategories.Commands
{
    public record MenuCategoryDeleteCommand(Guid Id) : ICommand
    {
    }
    public class MenuCategoryDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<MenuCategoryDeleteCommand>
    {
        public async Task Handle(MenuCategoryDeleteCommand command, CancellationToken cancellationToken)
        {
            MenuCategory? entity = await context.MenuCategories.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.MenuCategories.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
