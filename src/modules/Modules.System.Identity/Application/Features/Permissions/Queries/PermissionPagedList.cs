using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Permissions.Queries
{
    public record PermissionPagedListQuery : PageFilter<Permission, PermissionPagedListDto>, IQuery<PagedList<PermissionPagedListDto>>
    {
        public Guid ResourceId { get; set; }

    }
    public record PermissionPagedListDto : IMapFrom<Permission>, IQueryFilter<Permission>
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Resource { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }

        public static IQueryable<Permission> Apply(IQueryable<Permission> query, string text)
        {
            return query.Where(x => x.Code.Contains(text)  || x.Name!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<Permission, PermissionPagedListDto>()
                .ForMember(x=>x.Resource,opt=>opt.MapFrom(x=>x.Resource!.Title));
        }
    }
    public class PermissionPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<PermissionPagedListQuery, PagedList<PermissionPagedListDto>>
    {
        public async Task<PagedList<PermissionPagedListDto>> Handle(PermissionPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Permissions.AsNoTracking();
            if (request.ResourceId != Guid.Empty)
                _list = _list.Where(x => x.ResourceId == request.ResourceId);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
