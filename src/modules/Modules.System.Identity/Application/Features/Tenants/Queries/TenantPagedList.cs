using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Queries
{
    public record TenantPagedListQuery : PageFilter<Tenant, TenantPagedListDto>, IQuery<PagedList<TenantPagedListDto>>
    {
    }
    public record TenantPagedListDto : IMapFrom<Tenant>, IQueryFilter<Tenant>
    {
        public Guid Id { get; set; }
        public string Name { get; init; }
        public string? Domain { get; set; }
        public Guid? Logo { get; set; }
        public string Slug { get; init; }
        public bool IsActive { get; init; }

        public static IQueryable<Tenant> Apply(IQueryable<Tenant> query, string text)
        {
            return query.Where(x=>x.Slug.Contains(text)||x.Name!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Tenant, TenantPagedListDto>();
        }
    }
    public class TenantPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<TenantPagedListQuery, PagedList<TenantPagedListDto>>
    {
        public async Task<PagedList<TenantPagedListDto>> Handle(TenantPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Tenants.AsNoTracking();
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}

