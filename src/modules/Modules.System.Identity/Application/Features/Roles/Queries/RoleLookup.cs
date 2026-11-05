using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Roles.Queries
{
    public record RoleLookupQuery : IQuery<List<RoleLookupDto>>
    {
    }
    public record RoleLookupDto : MapFrom<Role>
    {
        public Guid Id { get; set; }
        public required string Title { get; set; }
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<Role, RoleLookupDto>()
                .ForMember(x => x.Title, opt => opt.MapFrom(x => x.Name));
        }
    }
    public class RoleLookupQueryHandler(IIdentityDbContext context, IMapper mapper, ICurrentUser auth) : IQueryHandler<RoleLookupQuery, List<RoleLookupDto>>
    {
        public async Task<List<RoleLookupDto>> Handle(RoleLookupQuery request, CancellationToken cancellationToken)
        {
            var u = auth.UserId;

            // System administrators are intentionally null-Tenant users.
            // Their Role definitions/assignments are stored under fixed
            // Master Tenant. A tenant query filter would return an empty
            // lookup, even after successful login.
            var master = global::Modules.System.Identity.Infrastructure.Persistence.DefaultSeedData.MasterTenant.Id;
            var res = await context.UserRoles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                .Where(x => x.UserId == auth.UserId && x.TenantId == master &&
                            x.Role.TenantId == master)
                .Select(x => x.Role)
                .ProjectToListAsync<RoleLookupDto>(mapper.ConfigurationProvider);
            return res;
        }
    }
}
