using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Commands
{
    public record TenantChangeStateCommand(Guid Id) : ICommand;
    public class TenantChangeStateCommandHandler(IIdentityDbContext context) : ICommandHandler<TenantChangeStateCommand>
    {
        public async Task Handle(TenantChangeStateCommand command, CancellationToken cancellationToken)
        {
            Tenant? entity = await context.Tenants.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            entity.IsActive=!entity.IsActive;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
