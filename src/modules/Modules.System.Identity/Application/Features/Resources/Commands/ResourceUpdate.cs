using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Commands
{
    public record ResourceUpdateCommand : MapTo<Resource>, ICommand
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
    public class ResourceUpdateCommandValidator : AbstractValidator<ResourceUpdateCommand>
    {
        public ResourceUpdateCommandValidator()
        {

        }
    }
    public class ResourceUpdateCommandHandler : ICommandHandler<ResourceUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ResourceUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(ResourceUpdateCommand command, CancellationToken cancellationToken)
        {
            Resource? entity = await _context.Resources
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
