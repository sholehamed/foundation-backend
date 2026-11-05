using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Commands
{
    public record TenantDeleteCommand(Guid Id) : ICommand;
    public class TenantDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<TenantDeleteCommand>
    {
        public async Task Handle(TenantDeleteCommand command, CancellationToken cancellationToken)
        {
            Tenant? entity = await context.Tenants.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.Tenants.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
