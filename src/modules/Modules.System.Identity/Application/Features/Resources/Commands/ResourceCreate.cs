using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Commands
{
    public record ResourceCreateCommand : MapTo<Resource>, ICommand<Guid>
    {
        public Guid ModuleId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
    public class ResourceCreateCommandValidator : AbstractValidator<ResourceCreateCommand>
    {
        public ResourceCreateCommandValidator()
        {

        }
    }
    public class ResourceCreateCommandHandler : ICommandHandler<ResourceCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ResourceCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(ResourceCreateCommand command, CancellationToken cancellationToken)
        {
            Resource entity = _mapper.Map<Resource>(command);
            await _context.Resources.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }


}
