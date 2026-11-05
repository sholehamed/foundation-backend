using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Commands
{
    public record ApiResourceDeleteCommand(Guid Id) : ICommand;
    public class ApiResourceDeleteCommandHandler(IIdentityDbContext context) : ICommandHandler<ApiResourceDeleteCommand>
    {
        public async Task Handle(ApiResourceDeleteCommand command, CancellationToken cancellationToken)
        {
            ApiResource? entity = await context.ApiResources.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
            Guard.Against.NotFound(command.Id, entity);
            context.ApiResources.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
