using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Commands
{
    public record ApiResourceCreateCommand : MapTo<ApiResource>, ICommand<Guid>
    {
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
    public class ApiResourceCreateCommandValidator : AbstractValidator<ApiResourceCreateCommand>
    {
        public ApiResourceCreateCommandValidator()
        {

        }
    }
    public class ApiResourceCreateCommandHandler : ICommandHandler<ApiResourceCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ApiResourceCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(ApiResourceCreateCommand command, CancellationToken cancellationToken)
        {
            ApiResource entity = _mapper.Map<ApiResource>(command);
            await _context.ApiResources.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }
}
