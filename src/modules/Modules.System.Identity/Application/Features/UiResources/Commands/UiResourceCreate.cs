using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Commands
{
    public record UiResourceCreateCommand : MapTo<UiResource>, ICommand<Guid>
    {
        public Guid ResourceId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public UiResourceType Type { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }
    public class UiResourceCreateCommandValidator : AbstractValidator<UiResourceCreateCommand>
    {
        public UiResourceCreateCommandValidator()
        {

        }
    }
    public class UiResourceCreateCommandHandler : ICommandHandler<UiResourceCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public UiResourceCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(UiResourceCreateCommand command, CancellationToken cancellationToken)
        {
            UiResource entity = _mapper.Map<UiResource>(command);
            await _context.UiResources.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }
}
