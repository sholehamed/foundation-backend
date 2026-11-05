using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Roles.System.Identity.Application.Features.Roles.Commands
{
    public record RoleCreateCommand : MapTo<Role>, ICommand<Guid>
    {
        [global::System.Text.Json.Serialization.JsonIgnore]
        public Guid StorageTenantId { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<RoleCreateCommand, Role>()
                .ForMember(x => x.NormalizedName, opt => opt.MapFrom(x => x.Name.Trim().ToUpperInvariant()));
        }
    }
    public class RoleCreateCommandValidator : AbstractValidator<RoleCreateCommand>
    {
        public RoleCreateCommandValidator()
        {

        }
    }
    public class RoleCreateCommandHandler : ICommandHandler<RoleCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public RoleCreateCommandHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(RoleCreateCommand command, CancellationToken cancellationToken)
        {
            if (command.StorageTenantId == Guid.Empty)
                throw new UnauthorizedAccessException("A verified storage scope is required.");
            Role entity = _mapper.Map<Role>(command);
            entity.TenantId = command.StorageTenantId;
            await _context.Roles.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }


}
