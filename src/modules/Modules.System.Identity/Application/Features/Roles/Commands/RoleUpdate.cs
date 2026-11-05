using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Roles.System.Identity.Application.Features.Roles.Commands
{
    public record RoleUpdateCommand : MapTo<Role>, ICommand
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<RoleUpdateCommand, Role>()
                .ForMember(x => x.NormalizedName, opt => opt.MapFrom(x => x.Name.Trim().ToUpperInvariant()));
        }
    }
    public class RoleUpdateCommandValidator : AbstractValidator<RoleUpdateCommand>
    {
        public RoleUpdateCommandValidator()
        {

        }
    }
    public class RoleUpdateCommandHandler : ICommandHandler<RoleUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public RoleUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(RoleUpdateCommand command, CancellationToken cancellationToken)
        {
            Role? entity = await _context.Roles
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
