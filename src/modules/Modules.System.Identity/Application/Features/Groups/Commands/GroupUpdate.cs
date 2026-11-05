using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Groups.System.Identity.Application.Features.Groups.Commands
{
    public record GroupUpdateCommand : MapTo<Group>, ICommand
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid TenantId { get; set; }
        public string NormalizedName { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<GroupUpdateCommand, Group>()
                 .ForMember(x => x.NormalizedName, opt => opt.MapFrom(x => x.Name.ToUpperInvariant()));
        }
    }
    public class GroupUpdateCommandValidator : AbstractValidator<GroupUpdateCommand>
    {
        public GroupUpdateCommandValidator()
        {

        }
    }
    public class GroupUpdateCommandHandler : ICommandHandler<GroupUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public GroupUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(GroupUpdateCommand command, CancellationToken cancellationToken)
        {
            Group? entity = await _context.Groups
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
