using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Groups.System.Identity.Application.Features.Groups.Commands
{
    public record GroupCreateCommand : MapTo<Group>, ICommand<Guid>
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<GroupCreateCommand, Group>()
                 .ForMember(x => x.NormalizedName, opt => opt.MapFrom(x => x.Name.ToUpperInvariant()));
        }
    }
    public class GroupCreateCommandValidator : AbstractValidator<GroupCreateCommand>
    {
        public GroupCreateCommandValidator()
        {

        }
    }
    public class GroupCreateCommandHandler : ICommandHandler<GroupCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public GroupCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(GroupCreateCommand command, CancellationToken cancellationToken)
        {
            Group entity = _mapper.Map<Group>(command);
            await _context.Groups.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }


}
