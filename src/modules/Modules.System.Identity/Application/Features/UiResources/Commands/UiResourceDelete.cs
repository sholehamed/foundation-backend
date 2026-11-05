using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Commands
{
    public record UiResourceDeleteCommand(Guid Id) : ICommand;
    public class UiResourceDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<UiResourceDeleteCommand>
    {
        public async Task Handle(UiResourceDeleteCommand command, CancellationToken cancellationToken)
        {
            UiResource? entity = await context.UiResources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.UiResources.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
