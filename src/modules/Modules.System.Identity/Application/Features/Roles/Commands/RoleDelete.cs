using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
namespace Roles.System.Identity.Application.Features.Roles.Commands
{
    public record RoleDeleteCommand(Guid Id) : ICommand;
    public class RoleDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<RoleDeleteCommand>
    {
        public async Task Handle(RoleDeleteCommand command, CancellationToken cancellationToken)
        {
            Role? entity = await context.Roles.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Roles.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
