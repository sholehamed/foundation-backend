using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Groups.System.Identity.Application.Features.Groups.Commands
{
    public record GroupChangeStateCommand(Guid Id) : ICommand;
    public class GroupChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<GroupChangeStateCommand>
    {
        public async Task Handle(GroupChangeStateCommand command, CancellationToken cancellationToken)
        {
            Group? entity = await context.Groups.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
