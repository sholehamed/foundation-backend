using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Commands
{
    public record ModuleDeleteCommand(Guid Id) : ICommand;
    public class ModuleDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<ModuleDeleteCommand>
    {
        public async Task Handle(ModuleDeleteCommand command, CancellationToken cancellationToken)
        {
            Module? entity = await context.Modules.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Modules.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
