using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Commands
{
    public record PermissionDeleteCommand(Guid Id) : ICommand;
    public class PermissionDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<PermissionDeleteCommand>
    {
        public async Task Handle(PermissionDeleteCommand command, CancellationToken cancellationToken)
        {
            Permission? entity = await context.Permissions.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Permissions.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
