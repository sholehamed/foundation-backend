using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Commands
{
    public record UserDeleteCommand(Guid Id) : ICommand;
    public class UserDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<UserDeleteCommand>
    {
        public async Task Handle(UserDeleteCommand command, CancellationToken cancellationToken)
        {
            User? entity = await context.Users.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Users.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
