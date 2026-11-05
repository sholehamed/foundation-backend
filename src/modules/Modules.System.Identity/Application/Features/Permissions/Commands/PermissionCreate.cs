using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Commands
{
   
    public record PermissionCreateCommand : MapTo<Permission>, ICommand<Guid>
    {
        public string Code { get; set; }
        public Guid ResourceId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public ICollection<PermissionResource>? PermissionResources { get; set; }

        public override void Mapping(Profile profile)
        {
            profile.CreateMap<PermissionCreateCommand, Permission>()
                .ForMember(x => x.ApiResources, opt => opt.MapFrom(x => (x.PermissionResources ?? new List<PermissionResource>()).Where(x=>x.Type=="API").Select(x => new PermissionApiResource { ApiResourceId = x.Id })))
                .ForMember(x => x.UiResources, opt => opt.MapFrom(x => (x.PermissionResources ?? new List<PermissionResource>()).Where(x => x.Type == "UI").Select(x => new PermissionUiResource { UiResourceId = x.Id })))
                ;
        }
    }
    public class PermissionCreateValidator : AbstractValidator<PermissionCreateCommand>
    {
        public PermissionCreateValidator()
        {

        }
    }
    public class PermissionCreateHandler : ICommandHandler<PermissionCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public PermissionCreateHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(PermissionCreateCommand command, CancellationToken cancellationToken)
        {

            Permission entity = _mapper.Map<Permission>(command);
            await _context.Permissions.AddAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }
}
