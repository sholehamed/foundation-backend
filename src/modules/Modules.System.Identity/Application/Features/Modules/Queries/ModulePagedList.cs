using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Modules.Queries
{
    public record ModulePagedListQuery : PageFilter<Module, ModulePagedListDto>, IQuery<PagedList<ModulePagedListDto>>
    {
    }
    public record ModulePagedListDto : IMapFrom<Module>, IQueryFilter<Module>
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public static IQueryable<Module> Apply(IQueryable<Module> query, string text)
        {
            return query.Where(x => x.Code.Contains(text) || x.Title!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Module, ModulePagedListDto>();
        }
    }
    public class ModulePagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<ModulePagedListQuery, PagedList<ModulePagedListDto>>
    {
        public async Task<PagedList<ModulePagedListDto>> Handle(ModulePagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Modules.AsNoTracking();
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
