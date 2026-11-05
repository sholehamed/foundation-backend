using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.ApiResources.Queries
{
    public record ApiResourcePagedListQuery : PageFilter<ApiResource, ApiResourcePagedListDto>, IQuery<PagedList<ApiResourcePagedListDto>>
    {
        public Guid ResourceId { get; set; }
    }
    public record ApiResourcePagedListDto : IMapFrom<ApiResource>, IQueryFilter<ApiResource>
    {
        public Guid Id { get; set; }
        public string Resource { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public string HttpMethod { get; set; }
        public string RouteTemplate { get; set; }
        public string Source { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeprecated { get; set; }

        public static IQueryable<ApiResource> Apply(IQueryable<ApiResource> query, string text)
        {
            return query.Where(x => x.Key.Contains(text) || x.Title!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<ApiResource, ApiResourcePagedListDto>()
                                .ForMember(x=>x.HttpMethod,opt=>opt.MapFrom(x=>x.HttpMethod.ToString()))
                                .ForMember(x=>x.Source,opt=>opt.MapFrom(x=>x.Source.ToString()))
                                .ForMember(x => x.Resource, opt => opt.MapFrom(x => x.Resource!.Title))
                                ;
        }
    }
    public class ApiResourcePagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<ApiResourcePagedListQuery, PagedList<ApiResourcePagedListDto>>
    {
        public async Task<PagedList<ApiResourcePagedListDto>> Handle(ApiResourcePagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.ApiResources.AsNoTracking();
            if (request.ResourceId != Guid.Empty)
                _list = _list.Where(x => x.ResourceId == request.ResourceId);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
