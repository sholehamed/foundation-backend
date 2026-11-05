using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Commands
{
    public record ModuleCreateCommand : MapTo<Module>, ICommand<Guid>
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
    public class ModuleCreateCommandValidator : AbstractValidator<ModuleCreateCommand>
    {
        public ModuleCreateCommandValidator()
        {

        }
    }
    public class ModuleCreateCommandHandler : ICommandHandler<ModuleCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public ModuleCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(ModuleCreateCommand command, CancellationToken cancellationToken)
        {
            Module entity = _mapper.Map<Module>(command);
            await _context.Modules.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }


}
