using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Commands
{
    public record ApiResourceUpdateCommand : MapTo<ApiResource>, ICommand
    {
        public Guid Id { get; set; }
        public Guid ResourceId { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public Domain.Entities.HttpMethod HttpMethod { get; set; }
        public string RouteTemplate { get; set; }
        public ApiSource Source { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeprecated { get; set; }
    }
    public class ApiResourceUpdateCommandValidator : AbstractValidator<ApiResourceUpdateCommand>
    {
        public ApiResourceUpdateCommandValidator()
        {

        }
    }
    public class ApiResourceUpdateCommandHandler : ICommandHandler<ApiResourceUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ApiResourceUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(ApiResourceUpdateCommand command, CancellationToken cancellationToken)
        {
            ApiResource? entity = await _context.ApiResources
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
