using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.Permissions.Commands;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Queries
{
    public record PermissionGetByIdQuery(Guid Id) : IQuery<PermissionGetByIdDto>
    {
    }

    public record PermissionGetByIdDto : MapFrom<Permission>
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public Guid ResourceId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public ICollection<PermissionResource>? PermissionResources { get; set; }

        public override void Mapping(Profile profile)
        {
            profile.CreateMap<Permission, PermissionGetByIdDto>()
                .ForMember(x => x.PermissionResources, opt => opt.MapFrom(x => x.ApiResources.Select(x => new PermissionResource { Id = x.ApiResourceId, Type = "API" }).Union(x.UiResources.Select(x => new PermissionResource { Id = x.UiResourceId, Type = "UI" }))));
        }

    }
    public class PermissionGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<PermissionGetByIdQuery, PermissionGetByIdDto>
    {
        public async Task<PermissionGetByIdDto> Handle(PermissionGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Permissions.AsNoTracking().Include(x=>x.UiResources).Include(x=>x.ApiResources)
                .Where(x => x.Id == request.Id)
                .ProjectTo<PermissionGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Permission not found.");
            return res;
        }
    }
}
