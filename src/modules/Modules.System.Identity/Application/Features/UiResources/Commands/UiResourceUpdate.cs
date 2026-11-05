using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Commands
{
    public record UiResourceUpdateCommand : MapTo<UiResource>, ICommand
    {
        public Guid Id { get; set; }
        public Guid ResourceId { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public UiResourceType Type { get; set; }


    }
    public class UiResourceUpdateCommandValidator : AbstractValidator<UiResourceUpdateCommand>
    {
        public UiResourceUpdateCommandValidator()
        {

        }
    }
    public class UiResourceUpdateCommandHandler : ICommandHandler<UiResourceUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public UiResourceUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(UiResourceUpdateCommand command, CancellationToken cancellationToken)
        {
            UiResource? entity = await _context.UiResources
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
