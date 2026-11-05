using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Commands
{
    public record ModuleChangeStateCommand(Guid Id) : ICommand;
    public class ModuleChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<ModuleChangeStateCommand>
    {
        public async Task Handle(ModuleChangeStateCommand command, CancellationToken cancellationToken)
        {
            Module? entity = await context.Modules.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
