using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Commands
{
    public record PermissionChangeStateCommand(Guid Id) : ICommand;
    public class PermissionChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<PermissionChangeStateCommand>
    {
        public async Task Handle(PermissionChangeStateCommand command, CancellationToken cancellationToken)
        {
            Permission? entity = await context.Permissions.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive = !entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
