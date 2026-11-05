using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
namespace Groups.System.Identity.Application.Features.Groups.Queries
{
    public record GroupPagedListQuery : PageFilter<Group, GroupPagedListDto>, IQuery<PagedList<GroupPagedListDto>>
    {
    }
    public record GroupPagedListDto : IMapFrom<Group>, IQueryFilter<Group>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        public static IQueryable<Group> Apply(IQueryable<Group> query, string text)
        {
            return query.Where(x =>  x.Name!.Contains(text));
         }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Group, GroupPagedListDto>();
        }
    }
    public class GroupPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<GroupPagedListQuery, PagedList<GroupPagedListDto>>
    {
        public async Task<PagedList<GroupPagedListDto>> Handle(GroupPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Groups.AsNoTracking();
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
