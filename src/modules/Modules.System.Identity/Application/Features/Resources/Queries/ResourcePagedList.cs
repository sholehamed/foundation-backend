using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Resources.Queries
{
    public record ResourcePagedListQuery : PageFilter<Resource, ResourcePagedListDto>, IQuery<PagedList<ResourcePagedListDto>>
    {
    }
    public record ResourcePagedListDto : IMapFrom<Resource>, IQueryFilter<Resource>
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Module { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public static IQueryable<Resource> Apply(IQueryable<Resource> query, string text)
        {
            return query.Where(x => x.Code.Contains(text) || x.Title!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Resource, ResourcePagedListDto>()
                .ForMember(x=>x.Module,opt=>opt.MapFrom(x=>x.Module!.Title));
        }
    }
    public class ResourcePagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<ResourcePagedListQuery, PagedList<ResourcePagedListDto>>
    {
        public async Task<PagedList<ResourcePagedListDto>> Handle(ResourcePagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Resources.AsNoTracking();
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
