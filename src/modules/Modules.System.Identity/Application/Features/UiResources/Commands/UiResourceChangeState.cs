using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Commands
{
    public record UiResourceChangeStateCommand(Guid Id) : ICommand;
    public class UiResourceChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<UiResourceChangeStateCommand>
    {
        public async Task Handle(UiResourceChangeStateCommand command, CancellationToken cancellationToken)
        {
            UiResource? entity = await context.UiResources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive = !entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
