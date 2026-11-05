using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
namespace Roles.System.Identity.Application.Features.Roles.Queries
{
    public record RolePagedListQuery : PageFilter<Role, RolePagedListDto>, IQuery<PagedList<RolePagedListDto>>
    {
    }
    public record RolePagedListDto : IMapFrom<Role>, IQueryFilter<Role>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        public static IQueryable<Role> Apply(IQueryable<Role> query, string text)
        {
            return query.Where(x =>  x.Name!.Contains(text));
         }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Role, RolePagedListDto>();
        }
    }
    public class RolePagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<RolePagedListQuery, PagedList<RolePagedListDto>>
    {
        public async Task<PagedList<RolePagedListDto>> Handle(RolePagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Roles.AsNoTracking().Where(x=>x.Scope==RoleScope.System);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
