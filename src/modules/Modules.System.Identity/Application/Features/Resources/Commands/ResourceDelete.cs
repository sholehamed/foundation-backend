using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Commands
{
    public record ResourceDeleteCommand(Guid Id) : ICommand;
    public class ResourceDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<ResourceDeleteCommand>
    {
        public async Task Handle(ResourceDeleteCommand command, CancellationToken cancellationToken)
        {
            Resource? entity = await context.Resources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Resources.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
