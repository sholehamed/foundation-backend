using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Commands
{
    public record MenuDeleteCommand(Guid Id) : ICommand
    {
    }
    public class MenuDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<MenuDeleteCommand>
    {
        public async Task Handle(MenuDeleteCommand command, CancellationToken cancellationToken)
        {
            Menu? entity = await context.Menus.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Menus.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
