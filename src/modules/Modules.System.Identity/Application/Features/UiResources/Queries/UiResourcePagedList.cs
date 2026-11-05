using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.UiResources.Queries
{
    public record UiResourcePagedListQuery : PageFilter<UiResource, UiResourcePagedListDto>, IQuery<PagedList<UiResourcePagedListDto>>
    {
        public Guid ResourceId { get; set; }
    }
    public record UiResourcePagedListDto : IMapFrom<UiResource>, IQueryFilter<UiResource>
    {
        public Guid Id { get; set; }
        public string Resource { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; }
        public bool IsActive { get; set; }

        public static IQueryable<UiResource> Apply(IQueryable<UiResource> query, string text)
        {
            return query.Where(x => x.Key.Contains(text) || x.Title!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<UiResource, UiResourcePagedListDto>()
                                .ForMember(x=>x.Type,opt=>opt.MapFrom(x=>x.Type!.ToString()))
                                .ForMember(x => x.Resource, opt => opt.MapFrom(x => x.Resource!.Title))
                                ;
        }
    }
    public class UiResourcePagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<UiResourcePagedListQuery, PagedList<UiResourcePagedListDto>>
    {
        public async Task<PagedList<UiResourcePagedListDto>> Handle(UiResourcePagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.UiResources.AsNoTracking();
            if (request.ResourceId != Guid.Empty)
                _list = _list.Where(x => x.ResourceId == request.ResourceId);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
