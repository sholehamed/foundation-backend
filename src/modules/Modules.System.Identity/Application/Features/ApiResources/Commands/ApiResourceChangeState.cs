using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Commands
{
    public record ApiResourceChangeStateCommand(Guid Id) : ICommand;
    public class ApiResourceChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<ApiResourceChangeStateCommand>
    {
        public async Task Handle(ApiResourceChangeStateCommand command, CancellationToken cancellationToken)
        {
            ApiResource? entity = await context.ApiResources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive = !entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
