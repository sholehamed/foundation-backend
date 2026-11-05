using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
namespace Groups.System.Identity.Application.Features.Groups.Commands
{
    public record GroupDeleteCommand(Guid Id) : ICommand;
    public class GroupDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<GroupDeleteCommand>
    {
        public async Task Handle(GroupDeleteCommand command, CancellationToken cancellationToken)
        {
            Group? entity = await context.Groups.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Groups.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
