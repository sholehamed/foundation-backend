using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Commands
{
    public record ModuleUpdateCommand : MapTo<Module>, ICommand
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
    public class ModuleUpdateCommandValidator : AbstractValidator<ModuleUpdateCommand>
    {
        public ModuleUpdateCommandValidator()
        {

        }
    }
    public class ModuleUpdateCommandHandler : ICommandHandler<ModuleUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ModuleUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(ModuleUpdateCommand command, CancellationToken cancellationToken)
        {
            Module? entity = await _context.Modules
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
