using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Queries
{
    public record TenantGetByIdQuery(Guid Id) : IQuery<TenantGetByIdDto>
    {
    }

    public record TenantGetByIdDto : MapFrom<Tenant>
    {
        public Guid Id { get; set; }
        public string Name { get; init; }
        public string? Domain { get; set; }
        public Guid? Logo { get; set; }
        public Guid? Favicon { get; set; }
        public string Slug { get; init; }
        public bool IsActive { get; init; }

    }
    public class TenantGetByIdQueryHandler(IMapper mapper, IIdentityDbContext context) : IQueryHandler<TenantGetByIdQuery, TenantGetByIdDto>
    {
        public async Task<TenantGetByIdDto> Handle(TenantGetByIdQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Tenants.AsNoTracking()
                .Where(x => x.Id == request.Id)
                .ProjectTo<TenantGetByIdDto>(mapper.ConfigurationProvider).FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("not found");
            return res;
        }
    }
}

