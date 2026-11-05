using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Commands
{
    public record UserChangeStateCommand(Guid Id) : ICommand;
    public class UserChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<UserChangeStateCommand>
    {
        public async Task Handle(UserChangeStateCommand command, CancellationToken cancellationToken)
        {
            User? entity = await context.Users.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
