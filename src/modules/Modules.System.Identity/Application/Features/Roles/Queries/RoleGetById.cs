using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Roles.System.Identity.Application.Features.Roles.Queries
{
    public record RoleGetByIdQuery(Guid Id) : IQuery<RoleGetByIdDto>
    {
    }

    public record RoleGetByIdDto : MapFrom<Role>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

    }
    public class RoleGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<RoleGetByIdQuery, RoleGetByIdDto>
    {
        public async Task<RoleGetByIdDto> Handle(RoleGetByIdQuery request, CancellationToken cancellationToken)
        {

            // This is a SystemAdministrator-only route. System principals
            // intentionally have no Tenant read scope. Bypass the *read*
            // filter only for the fixed Master role definition storage scope;
            // never grant a write bypass or expose foreign Tenant roles.
            var res = await context.Roles.IgnoreQueryFilters(["tenant"]).AsNoTracking()
                .Where(x => x.Id == request.Id &&
                    x.TenantId == Modules.System.Identity.Infrastructure.Persistence.DefaultSeedData.MasterTenant.Id)
                .ProjectTo<RoleGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}
