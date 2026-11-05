using Modules.System.Identity.Application.Features.ApiResources.Services;

namespace Modules.System.Identity.Application.Features.ApiResources.Commands
{
    public record ApiResourceSyncCommand(Guid ResourceId) : ICommand;

    public class ApiResourceSyncCommandHandler : ICommandHandler<ApiResourceSyncCommand>
    {
        private readonly ApiResourceSyncService _service;

        public ApiResourceSyncCommandHandler(ApiResourceSyncService service)
        {
            _service = service;
        }

        public async Task Handle(ApiResourceSyncCommand command, CancellationToken cancellationToken)
        {
            await _service.SyncMinimalApiResourcesAsync(command.ResourceId);
        }
    }
}
