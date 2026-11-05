using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Commands
{
    public record UserChangeLockStateCommand(Guid Id) : ICommand;
    public class UserChangeLockStateCommandHandler(IIdentityDbContext context) : ICommandHandler<UserChangeLockStateCommand>
    {
        public async Task Handle(UserChangeLockStateCommand command, CancellationToken cancellationToken)
        {
            User? entity = await context.Users.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsLockedOut = !entity.IsLockedOut;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
