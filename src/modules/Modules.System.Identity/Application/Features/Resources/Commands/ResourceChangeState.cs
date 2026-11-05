using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Commands
{
    public record ResourceChangeStateCommand(Guid Id) : ICommand;
    public class ResourceChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<ResourceChangeStateCommand>
    {
        public async Task Handle(ResourceChangeStateCommand command, CancellationToken cancellationToken)
        {
            Resource? entity = await context.Resources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
