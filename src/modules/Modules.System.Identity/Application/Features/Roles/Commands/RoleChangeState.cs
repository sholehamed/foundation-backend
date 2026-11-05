using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Roles.System.Identity.Application.Features.Roles.Commands
{
    public record RoleChangeStateCommand(Guid Id) : ICommand;
    public class RoleChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<RoleChangeStateCommand>
    {
        public async Task Handle(RoleChangeStateCommand command, CancellationToken cancellationToken)
        {
            Role? entity = await context.Roles.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
